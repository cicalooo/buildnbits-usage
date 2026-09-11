using System.Globalization;
using System.Text.Json;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;

namespace BuildnBits.Usage.Core.Providers.Grok;

public static class GrokBillingParser
{
    public const string BuildLabel = "Build";
    public const string BotLabel = "Bot";

    public static ProviderSnapshot Parse(JsonElement result, DateTimeOffset nowUtc)
    {
        var root = result;
        if (result.TryGetProperty("config", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            root = nested;
        }

        var resets = ReadReset(root);
        var plan = ReadString(result, "subscriptionTier") ?? ReadString(root, "subscriptionTier");
        var duration = ReadDurationMinutes(root);
        var windows = ReadProductWindows(root, resets, duration);

        if (windows.Count == 0)
        {
            var usedRaw = ReadUsedPercent(root);
            windows.Add(new UsageWindow(
                BuildLabel,
                duration,
                PercentageMath.ClampPercent(usedRaw),
                PercentageMath.RemainingFromUsed(usedRaw),
                resets));
        }

        return new ProviderSnapshot(ProviderKind.Grok, UsageStatus.Ok, plan, windows, nowUtc, null);
    }

    private static List<UsageWindow> ReadProductWindows(
        JsonElement root,
        DateTimeOffset? resets,
        int? duration)
    {
        var windows = new List<UsageWindow>(2);
        if (!root.TryGetProperty("productUsage", out var list) || list.ValueKind != JsonValueKind.Array)
        {
            return windows;
        }

        UsageWindow? build = null;
        UsageWindow? bot = null;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            if (!item.TryGetProperty("product", out var productEl))
            {
                continue;
            }

            var label = GrokProductMap.LabelFor(productEl);
            if (label is null || !TryReadUsagePercent(item, out var usedRaw))
            {
                continue;
            }

            var window = new UsageWindow(
                label,
                duration,
                PercentageMath.ClampPercent(usedRaw),
                PercentageMath.RemainingFromUsed(usedRaw),
                resets);

            if (label == BuildLabel)
            {
                build ??= window;
            }
            else if (label == BotLabel)
            {
                bot ??= window;
            }
        }

        if (build is not null)
        {
            windows.Add(build);
        }

        if (bot is not null)
        {
            windows.Add(bot);
        }

        return windows;
    }

    private static bool TryReadUsagePercent(JsonElement item, out double usedRaw)
    {
        usedRaw = 0;
        return item.TryGetProperty("usagePercent", out var p) &&
               p.ValueKind == JsonValueKind.Number &&
               p.TryGetDouble(out usedRaw);
    }

    private static int? ReadDurationMinutes(JsonElement root)
    {
        if (root.TryGetProperty("currentPeriod", out var period) && period.ValueKind == JsonValueKind.Object)
        {
            var type = ReadString(period, "type");
            if (type is not null && type.Contains("WEEKLY", StringComparison.OrdinalIgnoreCase))
            {
                return 10080;
            }
        }

        return null;
    }

    private static double ReadUsedPercent(JsonElement root)
    {
        if (TryGetDouble(root, "creditUsagePercent", out var percent))
        {
            return percent;
        }

        if (TryGetCent(root, "monthlyLimit", out var limit) &&
            TryGetCent(root, "used", out var used) &&
            limit > 0)
        {
            return used * 100.0 / limit;
        }

        throw new InvalidOperationException("Grok billing payload is missing usage percent.");
    }

    private static DateTimeOffset? ReadReset(JsonElement root)
    {
        if (root.TryGetProperty("currentPeriod", out var period) && period.ValueKind == JsonValueKind.Object)
        {
            var end = ReadString(period, "end");
            if (DateTimeOffset.TryParse(end, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            {
                return parsed.ToUniversalTime();
            }
        }

        var legacy = ReadString(root, "billingPeriodEnd");
        if (DateTimeOffset.TryParse(legacy, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var legacyParsed))
        {
            return legacyParsed.ToUniversalTime();
        }

        return null;
    }

    private static bool TryGetDouble(JsonElement el, string name, out double value)
    {
        value = 0;
        return el.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.Number && p.TryGetDouble(out value);
    }

    private static bool TryGetCent(JsonElement el, string name, out long value)
    {
        value = 0;
        if (!el.TryGetProperty(name, out var p))
        {
            return false;
        }

        if (p.ValueKind == JsonValueKind.Number)
        {
            return p.TryGetInt64(out value);
        }

        if (p.ValueKind == JsonValueKind.Object && p.TryGetProperty("val", out var val))
        {
            if (val.ValueKind == JsonValueKind.Number)
            {
                return val.TryGetInt64(out value);
            }

            value = 0;
            return true;
        }

        return false;
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
