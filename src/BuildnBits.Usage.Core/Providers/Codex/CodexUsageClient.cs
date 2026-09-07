using System.Text.Json;
using System.Text.Json.Nodes;
using BuildnBits.Usage.Core.JsonRpc;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Providers;

namespace BuildnBits.Usage.Core.Providers.Codex;

public sealed class CodexUsageClient : IUsageProvider, IDisposable, IAsyncDisposable
{
    private const string ClientVersion = "1.2.0";

    private readonly Func<string, IReadOnlyList<string>, JsonRpcProcessClient> _factory;
    private readonly string _executableName;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private JsonRpcProcessClient? _client;
    private string? _clientExecutable;
    private bool _initialized;
    private int _disposed;

    public CodexUsageClient(
        string executableName = "codex",
        Func<string, IReadOnlyList<string>, JsonRpcProcessClient>? factory = null)
    {
        _executableName = executableName;
        _factory = factory ?? ((file, args) => new JsonRpcProcessClient(new JsonRpcProcessOptions
        {
            FileName = file,
            Arguments = args,
            Timeout = TimeSpan.FromSeconds(25),
            EscapeForwardSlashes = true
        }));
    }

    public async Task<ProviderSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
        await _operationGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

            var exe = ProcessLocator.FindOnPath(_executableName);
            if (exe is null)
            {
                return new ProviderSnapshot(
                    ProviderKind.Codex,
                    UsageStatus.MissingCli,
                    null,
                    [],
                    DateTimeOffset.UtcNow,
                    "codex executable was not found on PATH.");
            }

            JsonRpcProcessClient client;
            try
            {
                client = await GetClientAsync(exe).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                return Error(ex.Message);
            }
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(25));

            try
            {
                if (!_initialized)
                {
                    await client.RequestAsync("initialize", new JsonObject
                    {
                        ["clientInfo"] = new JsonObject
                        {
                            ["name"] = "BuildnBits.Usage",
                            ["version"] = ClientVersion
                        },
                        ["capabilities"] = new JsonObject
                        {
                            ["optOutNotificationMethods"] = new JsonArray("account/rateLimits/updated")
                        }
                    }, timeout.Token).ConfigureAwait(false);
                    await client.NotifyAsync("initialized", new JsonObject(), timeout.Token).ConfigureAwait(false);
                    _initialized = true;
                }

                JsonNode? account = await client.RequestAsync(
                    "account/read",
                    new JsonObject { ["refreshToken"] = false },
                    timeout.Token).ConfigureAwait(false);

                if (account is not null)
                {
                    using var accountDoc = JsonDocument.Parse(account.ToJsonString());
                    CodexRateLimitsParser.AssertChatGptAuth(accountDoc.RootElement);

                    if (accountDoc.RootElement.TryGetProperty("account", out var acc) &&
                        acc.ValueKind is JsonValueKind.Null)
                    {
                        return new ProviderSnapshot(
                            ProviderKind.Codex,
                            UsageStatus.Unauthenticated,
                            null,
                            [],
                            DateTimeOffset.UtcNow,
                            "Codex is not signed in with a ChatGPT subscription.");
                    }
                }

                var limits = await client.RequestAsync(
                    "account/rateLimits/read",
                    null,
                    timeout.Token).ConfigureAwait(false);
                using var limitsDoc = JsonDocument.Parse((limits ?? new JsonObject()).ToJsonString());
                return CodexRateLimitsParser.Parse(limitsDoc.RootElement, DateTimeOffset.UtcNow);
            }
            catch (CodexApiKeyRejectedException ex)
            {
                return new ProviderSnapshot(
                    ProviderKind.Codex,
                    UsageStatus.AuthRejected,
                    null,
                    [],
                    DateTimeOffset.UtcNow,
                    ex.Message);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                await ResetClientAsync(client).ConfigureAwait(false);
                throw;
            }
            catch (Exception ex)
            {
                await ResetClientAsync(client).ConfigureAwait(false);
                return Error(ex.Message);
            }
        }
        finally
        {
            _operationGate.Release();
        }
    }

    private async Task<JsonRpcProcessClient> GetClientAsync(string executable)
    {
        if (_client is not null &&
            !_client.HasExited &&
            string.Equals(_clientExecutable, executable, StringComparison.OrdinalIgnoreCase))
        {
            return _client;
        }

        if (_client is not null)
        {
            await ResetClientAsync(_client).ConfigureAwait(false);
        }

        var client = _factory(executable, ["app-server", "--listen", "stdio://"]);
        _client = client;
        _clientExecutable = executable;
        _initialized = false;
        return client;
    }

    private async Task ResetClientAsync(JsonRpcProcessClient client)
    {
        if (ReferenceEquals(_client, client))
        {
            _client = null;
            _clientExecutable = null;
            _initialized = false;
        }

        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // A failed provider process is already unusable; do not mask the
            // provider status with a cleanup exception.
        }
    }

    private static ProviderSnapshot Error(string message) =>
        new(ProviderKind.Codex, UsageStatus.Error, null, [], DateTimeOffset.UtcNow, Sanitize(message));

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Codex request failed.";
        }

        if (message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return "Codex request failed.";
        }

        return message.Length <= 500 ? message : message[..497] + "...";
    }

    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        await _operationGate.WaitAsync().ConfigureAwait(false);
        JsonRpcProcessClient? client;
        try
        {
            client = _client;
            _client = null;
            _clientExecutable = null;
            _initialized = false;
        }
        finally
        {
            _operationGate.Release();
        }

        if (client is not null)
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }

        _operationGate.Dispose();
    }
}
