using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace BuildnBits.Usage.Core.JsonRpc;

public sealed class JsonRpcException : Exception
{
    public int? Code { get; }
    public JsonRpcException(string message, int? code = null, Exception? inner = null)
        : base(message, inner) => Code = code;
}

public sealed class JsonRpcProcessOptions
{
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(25);
    public bool EscapeForwardSlashes { get; init; }
}

/// <summary>
/// Speaks newline-delimited JSON-RPC 2.0 over a child process stdin/stdout.
/// Does not log response bodies that might contain credentials.
/// </summary>
public sealed class JsonRpcProcessClient : IAsyncDisposable
{
    private readonly Process _process;
    private readonly StreamWriter _stdin;
    private readonly StreamReader _stdout;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Dictionary<int, TaskCompletionSource<JsonNode>> _pending = [];
    private readonly object _sync = new();
    private int _nextId = 1;
    private readonly bool _escapeForwardSlashes;
    private readonly Task _readLoop;

    public JsonRpcProcessClient(JsonRpcProcessOptions options)
    {
        _escapeForwardSlashes = options.EscapeForwardSlashes;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var start = new ProcessStartInfo
        {
            FileName = options.FileName,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = utf8,
            StandardInputEncoding = utf8
        };
        foreach (var arg in options.Arguments)
        {
            start.ArgumentList.Add(arg);
        }

        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!_process.Start())
            {
                throw new JsonRpcException($"Failed to start {options.FileName}.");
            }
        }
        catch (Exception ex) when (ex is not JsonRpcException)
        {
            throw new JsonRpcException($"Failed to start {options.FileName}: {ex.Message}", inner: ex);
        }

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;
        _ = DrainErrorAsync();
        _readLoop = ReadLoopAsync(_lifetime.Token);
    }

    public async Task<JsonNode?> RequestAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _pending[id] = tcs;
        }

        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method
        };
        if (parameters is not null)
        {
            payload["params"] = parameters;
        }

        try
        {
            await WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            lock (_sync)
            {
                _pending.Remove(id);
            }

            throw;
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime.Token);
        await using var reg = linked.Token.Register(() => tcs.TrySetCanceled(linked.Token));
        try
        {
            return await tcs.Task.ConfigureAwait(false);
        }
        catch (OperationCanceledException ex)
        {
            throw new JsonRpcException("JSON-RPC request timed out or the process ended.", inner: ex);
        }
    }

    public Task NotifyAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        var payload = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = method
        };
        if (parameters is not null)
        {
            payload["params"] = parameters;
        }

        return WriteAsync(payload, cancellationToken);
    }

    private async Task WriteAsync(JsonObject payload, CancellationToken cancellationToken)
    {
        var json = payload.ToJsonString(new JsonSerializerOptions
        {
            Encoder = _escapeForwardSlashes
                ? System.Text.Encodings.Web.JavaScriptEncoder.Default
                : System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        if (!_escapeForwardSlashes)
        {
            json = json.Replace("\\/", "/", StringComparison.Ordinal);
        }

        await _stdin.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
        await _stdin.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
        await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task ReadLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await _stdout.ReadLineAsync(cancellationToken).ConfigureAwait(false);
                if (line is null)
                {
                    break;
                }

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonNode? node;
                try
                {
                    node = JsonNode.Parse(line);
                }
                catch (JsonException)
                {
                    continue;
                }

                if (node is not JsonObject obj)
                {
                    continue;
                }

                var hasMethod = obj["method"] is JsonValue;
                if (obj["id"] is JsonValue idNode && TryGetId(idNode, out var id))
                {
                    TaskCompletionSource<JsonNode>? tcs;
                    lock (_sync)
                    {
                        _pending.Remove(id, out tcs);
                    }

                    if (tcs is not null)
                    {
                        if (obj["error"] is JsonObject error)
                        {
                            var message = error["message"]?.GetValue<string>() ?? "JSON-RPC error";
                            var code = error["code"]?.GetValue<int>();
                            if (code == 429 || message.Contains("429", StringComparison.Ordinal))
                            {
                                tcs.TrySetException(new JsonRpcException("Rate limited (429).", 429));
                            }
                            else
                            {
                                tcs.TrySetException(new JsonRpcException(message, code));
                            }
                        }
                        else
                        {
                            tcs.TrySetResult(obj["result"] ?? new JsonObject());
                        }

                        continue;
                    }

                    if (hasMethod)
                    {
                        // Server-to-client request. Acknowledge so the peer does not stall.
                        var reply = new JsonObject
                        {
                            ["jsonrpc"] = "2.0",
                            ["id"] = id,
                            ["error"] = new JsonObject
                            {
                                ["code"] = -32601,
                                ["message"] = "not implemented"
                            }
                        };
                        try
                        {
                            await WriteAsync(reply, cancellationToken).ConfigureAwait(false);
                        }
                        catch
                        {
                            // ignored
                        }
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }
        finally
        {
            FailAll(new JsonRpcException("JSON-RPC process closed."));
        }
    }

    private async Task DrainErrorAsync()
    {
        try
        {
            while (await _process.StandardError.ReadLineAsync().ConfigureAwait(false) is { })
            {
                // Discard stderr so the pipe does not fill. Never copy credentials.
            }
        }
        catch
        {
            // ignored
        }
    }

    private static bool TryGetId(JsonValue idNode, out int id)
    {
        if (idNode.TryGetValue<int>(out id))
        {
            return true;
        }

        if (idNode.TryGetValue<long>(out var longId) && longId is >= int.MinValue and <= int.MaxValue)
        {
            id = (int)longId;
            return true;
        }

        id = 0;
        return false;
    }

    private void FailAll(Exception exception)
    {
        List<TaskCompletionSource<JsonNode>> pending;
        lock (_sync)
        {
            pending = [.. _pending.Values];
            _pending.Clear();
        }

        foreach (var tcs in pending)
        {
            tcs.TrySetException(exception);
        }
    }

    public async ValueTask DisposeAsync()
    {
        _lifetime.Cancel();
        try
        {
            _stdin.Close();
        }
        catch
        {
            // ignored
        }

        try
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // ignored
        }

        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch
        {
            // ignored
        }

        _process.Dispose();
        _lifetime.Dispose();
    }
}
