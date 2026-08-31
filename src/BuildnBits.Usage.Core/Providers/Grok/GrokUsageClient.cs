using System.Text.Json;
using System.Text.Json.Nodes;
using BuildnBits.Usage.Core.JsonRpc;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Providers;

namespace BuildnBits.Usage.Core.Providers.Grok;

public sealed class GrokUsageClient : IUsageProvider
{
    private readonly Func<string, IReadOnlyList<string>, JsonRpcProcessClient> _factory;
    private readonly string _executableName;

    public GrokUsageClient(
        string executableName = "grok",
        Func<string, IReadOnlyList<string>, JsonRpcProcessClient>? factory = null)
    {
        _executableName = executableName;
        _factory = factory ?? ((file, args) => new JsonRpcProcessClient(new JsonRpcProcessOptions
        {
            FileName = file,
            Arguments = args,
            Timeout = TimeSpan.FromSeconds(25),
            EscapeForwardSlashes = false
        }));
    }

    public async Task<ProviderSnapshot> FetchAsync(CancellationToken cancellationToken)
    {
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
            client = _factory(exe, ["--no-auto-update", "agent", "stdio"]);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        await using var _ = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(20));

        JsonNode? init;
        try
        {
            init = await client.RequestAsync("initialize", new JsonObject
            {
                ["protocolVersion"] = 1,
                ["clientInfo"] = new JsonObject
                {
                    ["name"] = "BuildnBits.Usage",
                    ["version"] = "1.0.0"
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
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        if (!HasCachedToken(init))
        {
            return new ProviderSnapshot(
                ProviderKind.Grok,
                UsageStatus.Unauthenticated,
                null,
                [],
                DateTimeOffset.UtcNow,
                "Grok cached_token is unavailable. Run grok login; this app never reads credentials.");
        }

        JsonNode? auth = null;
        try
        {
            auth = await client.RequestAsync("authenticate", new JsonObject
            {
                ["methodId"] = "cached_token",
                ["authMethodId"] = "cached_token"
            }, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new ProviderSnapshot(
                ProviderKind.Grok,
                UsageStatus.Unauthenticated,
                null,
                [],
                DateTimeOffset.UtcNow,
                Sanitize(ex.Message));
        }

        var plan = auth?["_meta"]?["subscription_tier"]?.GetValue<string>();

        JsonNode? billing = await TryBillingAsync(client, timeout.Token).ConfigureAwait(false);
        if (billing is null)
        {
            try
            {
                using var sessionTimeout = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token);
                sessionTimeout.CancelAfter(TimeSpan.FromSeconds(8));
                await client.RequestAsync("session/new", new JsonObject
                {
                    ["cwd"] = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ["mcpServers"] = new JsonArray()
                }, sessionTimeout.Token).ConfigureAwait(false);
                billing = await TryBillingAsync(client, timeout.Token).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Session creation is optional; billing may already have failed with -32601.
            }
        }

        if (billing is null)
        {
            return Error("Grok billing method was not found on agent stdio.");
        }

        using var doc = JsonDocument.Parse(billing.ToJsonString());
        try
        {
            var snapshot = GrokBillingParser.Parse(doc.RootElement, DateTimeOffset.UtcNow);
            return snapshot with { PlanLabel = snapshot.PlanLabel ?? plan };
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
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
        if (message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("auth.json", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("bearer", StringComparison.OrdinalIgnoreCase))
        {
            return "Grok request failed.";
        }

        return message;
    }
}
