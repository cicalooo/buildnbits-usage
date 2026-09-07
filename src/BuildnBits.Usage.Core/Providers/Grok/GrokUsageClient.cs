using System.Text.Json;
using System.Text.Json.Nodes;
using BuildnBits.Usage.Core.JsonRpc;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Providers;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Core.Providers.Grok;

public sealed class GrokUsageClient : IUsageProvider, IDisposable, IAsyncDisposable
{
    private const string ClientVersion = "1.2.1";

    private readonly Func<string, IReadOnlyList<string>, JsonRpcProcessClient> _factory;
    private readonly string _executableName;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private JsonRpcProcessClient? _client;
    private string? _clientExecutable;
    private bool _initialized;
    private bool _cachedTokenAvailable;
    private int _disposed;

    public GrokUsageClient(
        string executableName = "grok",
        Func<string, IReadOnlyList<string>, JsonRpcProcessClient>? factory = null,
        AppLog? appLog = null)
    {
        _executableName = executableName;
        _factory = factory ?? ((file, args) => new JsonRpcProcessClient(new JsonRpcProcessOptions
        {
            FileName = file,
            Arguments = args,
            Timeout = TimeSpan.FromSeconds(25),
            EscapeForwardSlashes = false,
            DiagnosticLog = message => appLog?.Info($"Grok {message}")
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
                    ProviderKind.Grok,
                    UsageStatus.MissingCli,
                    null,
                    [],
                    DateTimeOffset.UtcNow,
                    "grok executable was not found on PATH.");
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
            timeout.CancelAfter(TimeSpan.FromSeconds(20));

            try
            {
                if (!_initialized)
                {
                    var init = await client.RequestAsync("initialize", new JsonObject
                    {
                        ["protocolVersion"] = 1,
                        ["clientInfo"] = new JsonObject
                        {
                            ["name"] = "BuildnBits.Usage",
                            ["version"] = ClientVersion
                        },
                        ["capabilities"] = new JsonObject(),
                        ["clientCapabilities"] = new JsonObject
                        {
                            ["fs"] = new JsonObject
                            {
                                ["readTextFile"] = false,
                                ["writeTextFile"] = false
                            },
                            ["terminal"] = false
                        }
                    }, timeout.Token).ConfigureAwait(false);
                    await client.NotifyAsync("initialized", new JsonObject(), timeout.Token).ConfigureAwait(false);
                    _cachedTokenAvailable = HasCachedToken(init);
                    _initialized = true;
                }

                if (!_cachedTokenAvailable)
                {
                    return new ProviderSnapshot(
                        ProviderKind.Grok,
                        UsageStatus.Unauthenticated,
                        null,
                        [],
                        DateTimeOffset.UtcNow,
                        "Grok cached_token is unavailable. Run grok login; this app never reads credentials.");
                }

                JsonNode? auth = await client.RequestAsync("authenticate", new JsonObject
                {
                    ["methodId"] = "cached_token",
                    ["authMethodId"] = "cached_token"
                }, timeout.Token).ConfigureAwait(false);

                var plan = auth?["_meta"]?["subscription_tier"]?.GetValue<string>();
                JsonNode? billing = await TryBillingAsync(client, timeout.Token).ConfigureAwait(false);
                if (billing is null)
                {
                    using var sessionTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                    sessionTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                    try
                    {
                        await client.RequestAsync("session/new", new JsonObject
                        {
                            ["cwd"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                            ["mcpServers"] = new JsonArray()
                        }, sessionTimeout.Token).ConfigureAwait(false);
                        billing = await TryBillingAsync(client, timeout.Token).ConfigureAwait(false);
                    }
                    catch (Exception)
                    {
                        // Session creation is optional; billing may already
                        // have failed with -32601.
                    }
                }

                if (billing is null)
                {
                    return Error("Grok billing method was not found on agent stdio.");
                }

                using var doc = JsonDocument.Parse(billing.ToJsonString());
                var snapshot = GrokBillingParser.Parse(doc.RootElement, DateTimeOffset.UtcNow);
                return snapshot with { PlanLabel = snapshot.PlanLabel ?? plan };
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

        var client = _factory(executable, ["--no-auto-update", "agent", "stdio"]);
        _client = client;
        _clientExecutable = executable;
        _initialized = false;
        _cachedTokenAvailable = false;
        return client;
    }

    private async Task ResetClientAsync(JsonRpcProcessClient client)
    {
        if (ReferenceEquals(_client, client))
        {
            _client = null;
            _clientExecutable = null;
            _initialized = false;
            _cachedTokenAvailable = false;
        }

        try
        {
            await client.DisposeAsync().ConfigureAwait(false);
        }
        catch
        {
            // Do not mask the provider failure with cleanup errors.
        }
    }

    private static async Task<JsonNode?> TryBillingAsync(JsonRpcProcessClient client, CancellationToken cancellationToken)
    {
        foreach (var (method, parameters) in BillingAttempts())
        {
            try
            {
                return await client.RequestAsync(method, parameters, cancellationToken).ConfigureAwait(false);
            }
            catch (JsonRpcException ex) when (ex.Code == -32601)
            {
                // try next method name
            }
        }

        return null;
    }

    private static IEnumerable<(string Method, JsonObject Parameters)> BillingAttempts()
    {
        yield return ("x.ai/billing", new JsonObject());
        yield return ("_x.ai/billing", new JsonObject());
        yield return ("session/request", new JsonObject
        {
            ["method"] = "x.ai/billing",
            ["params"] = new JsonObject()
        });
    }

    private static bool HasCachedToken(JsonNode? init)
    {
        var methods = init?["authMethods"] as JsonArray;
        if (methods is null)
        {
            return false;
        }

        foreach (var method in methods)
        {
            var id = method?["id"]?.GetValue<string>();
            if (string.Equals(id, "cached_token", StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static ProviderSnapshot Error(string message) =>
        new(ProviderKind.Grok, UsageStatus.Error, null, [], DateTimeOffset.UtcNow, Sanitize(message));

    private static string Sanitize(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Grok request failed.";
        }

        if (message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("auth.json", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return "Grok request failed.";
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
            _cachedTokenAvailable = false;
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
