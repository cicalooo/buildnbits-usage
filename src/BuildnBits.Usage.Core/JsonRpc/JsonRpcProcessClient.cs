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
    public Action<string>? DiagnosticLog { get; init; }
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
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private int _nextId = 1;
    private readonly bool _escapeForwardSlashes;
    private readonly Action<string>? _diagnosticLog;
    private readonly Task _readLoop;
    private WindowsProcessJob? _job;
    private int _disposed;

    public JsonRpcProcessClient(JsonRpcProcessOptions options)
    {
        _escapeForwardSlashes = options.EscapeForwardSlashes;
        _diagnosticLog = options.DiagnosticLog;
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        var launch = ProcessLocator.PrepareLaunch(options.FileName, options.Arguments);
        var start = ProcessLocator.CreateStartInfo(options.FileName, options.Arguments, redirectStandardInput: true);
        start.StandardInputEncoding = utf8;

        _process = new Process { StartInfo = start, EnableRaisingEvents = true };
        try
        {
            if (!_process.Start())
            {
                throw new JsonRpcException($"Failed to start {Path.GetFileName(options.FileName)}.");
            }

            _job = WindowsProcessJob.Create();
            var jobAssigned = _job?.TryAssign(_process) == true;
            if (!jobAssigned)
            {
                _job?.Dispose();
                _job = null;
            }

            TryLog(
                $"Process started: {ProcessLocator.DescribeLaunch(options.FileName)} " +
                $"launcher={Path.GetFileName(launch.FileName)} pid={_process.Id} " +
                $"job={(jobAssigned ? "assigned" : "unavailable")}.");
        }
        catch (Exception ex) when (ex is not JsonRpcException)
        {
            throw new JsonRpcException(
                $"Failed to start {Path.GetFileName(options.FileName)}: {ex.Message}",
                inner: ex);
        }

        _stdin = _process.StandardInput;
        _stdout = _process.StandardOutput;
        _ = DrainErrorAsync();
        _readLoop = ReadLoopAsync(_lifetime.Token);
    }

    public bool HasExited
    {
        get
        {
            try
            {
                return _process.HasExited;
            }
            catch (InvalidOperationException)
            {
                return true;
            }
        }
    }

    public async Task<JsonNode?> RequestAsync(string method, JsonNode? parameters, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
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
        finally
        {
            lock (_sync)
            {
                _pending.Remove(id);
            }
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
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
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

        await _writeGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stdin.WriteAsync(json.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.WriteAsync("\n".AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stdin.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
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
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

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

        TryLog($"Process disposed: pid={SafeProcessId()}.");
        _process.Dispose();
        _job?.Dispose();
        _job = null;
        _lifetime.Dispose();
        _writeGate.Dispose();
    }

    private int SafeProcessId()
    {
        try
        {
            return _process.Id;
        }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException)
        {
            return 0;
        }
    }

    private void TryLog(string message)
    {
        try
        {
            _diagnosticLog?.Invoke(message);
        }
        catch
        {
            // Diagnostics must never break process communication or cleanup.
        }
    }
}
