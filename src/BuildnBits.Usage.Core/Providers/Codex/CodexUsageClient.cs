using System.Text.Json;
using System.Text.Json.Nodes;
using BuildnBits.Usage.Core.JsonRpc;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Providers;

namespace BuildnBits.Usage.Core.Providers.Codex;

public sealed class CodexUsageClient : IUsageProvider
{
    private readonly Func<string, IReadOnlyList<string>, JsonRpcProcessClient> _factory;
    private readonly string _executableName;

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
            client = _factory(exe, ["app-server", "--listen", "stdio://"]);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        await using var _ = client;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(25));

        try
        {
            await client.RequestAsync("initialize", new JsonObject
        {
            ["clientInfo"] = new JsonObject
            {
                ["name"] = "BuildnBits.Usage",
                ["version"] = "1.0.0"
            },
            ["capabilities"] = new JsonObject
            {
                ["optOutNotificationMethods"] = new JsonArray("account/rateLimits/updated")
            }
            }, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        await client.NotifyAsync("initialized", new JsonObject(), timeout.Token).ConfigureAwait(false);

        JsonNode? account;
        try
        {
            account = await client.RequestAsync("account/read", new JsonObject { ["refreshToken"] = false }, timeout.Token)
                .ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        if (account is not null)
        {
            using var accountDoc = JsonDocument.Parse(account.ToJsonString());
            try
            {
                CodexRateLimitsParser.AssertChatGptAuth(accountDoc.RootElement);
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

            if (accountDoc.RootElement.TryGetProperty("account", out var acc) && acc.ValueKind is JsonValueKind.Null)
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

        JsonNode? limits;
        try
        {
            limits = await client.RequestAsync("account/rateLimits/read", null, timeout.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return Error(ex.Message);
        }

        using var limitsDoc = JsonDocument.Parse((limits ?? new JsonObject()).ToJsonString());
        try
        {
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
        catch (Exception ex)
        {
            return Error(ex.Message);
        }
    }

    private static ProviderSnapshot Error(string message) =>
        new(ProviderKind.Codex, UsageStatus.Error, null, [], DateTimeOffset.UtcNow, Sanitize(message));

    private static string Sanitize(string message)
    {
        if (message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("authorization", StringComparison.OrdinalIgnoreCase))
        {
            return "Codex request failed.";
        }

        return message;
    }
}
