using System.Text.Json;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;

namespace BuildnBits.Usage.Core.Providers.Codex;

public static class CodexWindowDurations
{
    public const int FiveHourMinutes = 300;
    public const int SevenDayMinutes = 10080;
}

public static class CodexRateLimitsParser
{
    public static ProviderSnapshot Parse(JsonElement result, DateTimeOffset nowUtc)
    {
        if (TryGetAuthMode(result, out var authMode) &&
            string.Equals(authMode, "apikey", StringComparison.OrdinalIgnoreCase))
        {
            throw new CodexApiKeyRejectedException();
        }

        JsonElement limits = default;
        var found = false;
        if (result.TryGetProperty("rateLimits", out var rateLimits) && rateLimits.ValueKind == JsonValueKind.Object)
        {
            limits = rateLimits;
            found = true;
        }
        else if (result.TryGetProperty("rateLimitsByLimitId", out var byId) && byId.ValueKind == JsonValueKind.Object)
        {
            if (byId.TryGetProperty("codex", out var codex))
            {
                limits = codex;
                found = true;
            }
        }

        if (!found)
        {
            throw new InvalidOperationException("Codex rate limit payload is missing.");
        }

        var windows = new List<UsageWindow>();
        CollectWindow(limits, "primary", windows);
        CollectWindow(limits, "secondary", windows);

        if (result.TryGetProperty("rateLimitsByLimitId", out var all) && all.ValueKind == JsonValueKind.Object)
        {
            foreach (var bucket in all.EnumerateObject())
            {
                CollectWindow(bucket.Value, "primary", windows);
                CollectWindow(bucket.Value, "secondary", windows);
            }
        }

        windows = DeduplicateByDuration(windows);
        var plan = ReadPlan(limits) ?? ReadPlan(result);
        return new ProviderSnapshot(
            ProviderKind.Codex,
            UsageStatus.Ok,
            plan,
            windows,
            nowUtc,
            null);
    }

    public static void AssertChatGptAuth(JsonElement accountReadResult)
    {
        var auth = ReadString(accountReadResult, "authMode")
                   ?? ReadNestedAuth(accountReadResult);
        if (string.Equals(auth, "apikey", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(auth, "api_key", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(auth, "ApiKey", StringComparison.Ordinal))
        {
            throw new CodexApiKeyRejectedException();
        }

        if (accountReadResult.TryGetProperty("account", out var account) &&
            account.ValueKind == JsonValueKind.Object)
        {
            var type = ReadString(account, "type") ?? ReadString(account, "authMode");
            if (string.Equals(type, "apikey", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(type, "api_key", StringComparison.OrdinalIgnoreCase))
            {
                throw new CodexApiKeyRejectedException();
            }
        }
    }

    private static void CollectWindow(JsonElement parent, string name, List<UsageWindow> windows)
    {
        if (!parent.TryGetProperty(name, out var node) || node.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        if (!node.TryGetProperty("usedPercent", out var usedEl) || !usedEl.TryGetDouble(out var used))
        {
            return;
        }

        int? duration = null;
        if (node.TryGetProperty("windowDurationMins", out var dur) && dur.ValueKind == JsonValueKind.Number && dur.TryGetInt32(out var mins))
        {
            duration = mins;
        }

        DateTimeOffset? resets = null;
        if (node.TryGetProperty("resetsAt", out var resetsEl) && resetsEl.ValueKind == JsonValueKind.Number && resetsEl.TryGetInt64(out var unix))
        {
            resets = DateTimeOffset.FromUnixTimeSeconds(unix);
        }

        var label = duration switch
        {
            CodexWindowDurations.FiveHourMinutes => "5-hour",
            CodexWindowDurations.SevenDayMinutes => "7-day",
            15 => "15-minute",
            60 => "1-hour",
            _ when duration is not null => $"{duration}-minute",
            _ => name
        };

        windows.Add(new UsageWindow(
            label,
            duration,
            PercentageMath.ClampPercent(used),
            PercentageMath.RemainingFromUsed(used),
            resets));
    }

    private static List<UsageWindow> DeduplicateByDuration(List<UsageWindow> windows)
    {
        var seen = new HashSet<int>();
        var result = new List<UsageWindow>();
        foreach (var window in windows)
        {
            if (window.DurationMinutes is { } mins)
            {
                if (!seen.Add(mins))
                {
                    continue;
                }
            }

            result.Add(window);
        }

        return result;
    }

    private static bool TryGetAuthMode(JsonElement result, out string authMode)
    {
        authMode = ReadString(result, "authMode") ?? "";
        return authMode.Length > 0;
    }

    private static string? ReadPlan(JsonElement el)
    {
        return ReadString(el, "planType") ?? ReadString(el, "plan");
    }

    private static string? ReadNestedAuth(JsonElement accountReadResult)
    {
        if (accountReadResult.TryGetProperty("account", out var account) && account.ValueKind == JsonValueKind.Object)
        {
            return ReadString(account, "authMode") ?? ReadString(account, "type");
        }

        return null;
    }

    private static string? ReadString(JsonElement el, string name)
    {
        if (el.ValueKind == JsonValueKind.Object &&
            el.TryGetProperty(name, out var p) &&
            p.ValueKind == JsonValueKind.String)
        {
            return p.GetString();
        }

        return null;
    }
}

public sealed class CodexApiKeyRejectedException : InvalidOperationException
{
    public CodexApiKeyRejectedException()
        : base("Codex API-key authentication is not supported. Sign in with a ChatGPT subscription.")
    {
    }
}
