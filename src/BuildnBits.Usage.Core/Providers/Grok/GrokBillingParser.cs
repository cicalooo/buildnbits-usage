using System.Globalization;
using System.Text.Json;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;

namespace BuildnBits.Usage.Core.Providers.Grok;

public static class GrokBillingParser
{
    private const int WeeklyPeriodType = 2;

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
        var durationMinutes = ReadDurationMinutes(root);
        var productWindows = ReadProductWindows(root, durationMinutes, resets);
        IReadOnlyList<UsageWindow> windows = productWindows ?? [];
        if (productWindows is null)
        {
            var usedRaw = ReadUsedPercent(root, nowUtc);
            var used = PercentageMath.ClampPercent(usedRaw);
            var remaining = PercentageMath.RemainingFromUsed(usedRaw);
            windows = [new UsageWindow(BuildLabel, durationMinutes, used, remaining, resets)];
        }

        var plan = ReadString(result, "subscriptionTier") ?? ReadString(root, "subscriptionTier");
        return new ProviderSnapshot(ProviderKind.Grok, UsageStatus.Ok, plan, windows, nowUtc, null);
    }

    private static IReadOnlyList<UsageWindow>? ReadProductWindows(
        JsonElement root,
        int? durationMinutes,
        DateTimeOffset? resetsAtUtc)
    {
        if (!root.TryGetProperty("productUsage", out var products))
        {
            return null;
        }

        if (products.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        if (products.GetArrayLength() == 0)
        {
            return null;
        }

        UsageWindow? build = null;
        UsageWindow? bot = null;
        foreach (var product in products.EnumerateArray())
        {
            if (product.ValueKind != JsonValueKind.Object ||
                !product.TryGetProperty("product", out var productValue))
            {
                continue;
            }

            var label = GrokProductMap.LabelFor(productValue);
            if (label is null ||
                !TryGetDouble(product, "usagePercent", out var usedRaw))
            {
                continue;
            }

            var window = new UsageWindow(
                label,
                durationMinutes,
                PercentageMath.ClampPercent(usedRaw),
                PercentageMath.RemainingFromUsed(usedRaw),
                resetsAtUtc);

            if (label == BuildLabel)
            {
                build ??= window;
            }
            else if (label == BotLabel)
            {
                bot ??= window;
            }
        }

        var windows = new List<UsageWindow>(2);
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

    private static int? ReadDurationMinutes(JsonElement root)
    {
        if (root.TryGetProperty("currentPeriod", out var period) && period.ValueKind == JsonValueKind.Object)
        {
            if (ReadString(period, "type") is { } type && IsWeeklyPeriodType(type))
            {
                return 10080;
            }

            if (period.TryGetProperty("type", out var numericType) &&
                numericType.ValueKind == JsonValueKind.Number &&
                numericType.TryGetInt32(out var typeId) &&
                typeId == WeeklyPeriodType)
            {
                return 10080;
            }
        }

        return null;
    }

    private static bool IsWeeklyPeriodType(string type) =>
        string.Equals(type, "USAGE_PERIOD_TYPE_WEEKLY", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(type, "WEEKLY", StringComparison.OrdinalIgnoreCase);

    private static double ReadUsedPercent(JsonElement root, DateTimeOffset nowUtc)
    {
        if (root.TryGetProperty("creditUsagePercent", out _))
        {
            if (TryGetDouble(root, "creditUsagePercent", out var percent))
            {
                return percent;
            }

            throw new InvalidOperationException("Grok billing payload has invalid usage percent.");
        }

        var hasLimit = root.TryGetProperty("monthlyLimit", out _);
        var hasUsed = root.TryGetProperty("used", out _);
        if (hasLimit || hasUsed)
        {
            if (TryGetCent(root, "monthlyLimit", out var limit) &&
                TryGetCent(root, "used", out var used) &&
                limit > 0)
            {
                return used * 100.0 / limit;
            }

            throw new InvalidOperationException("Grok billing payload has invalid legacy usage counters.");
        }

        // Proto JSON omits zero-valued usage fields at the start of an active
        // period. Require the exact unified-billing shape before treating that
        // omission as zero; an arbitrary missing field remains an error.
        if (HasActiveCurrentPeriod(root, nowUtc))
        {
            return 0;
        }

        throw new InvalidOperationException("Grok billing payload is missing usage percent.");
    }

    private static bool HasActiveCurrentPeriod(JsonElement root, DateTimeOffset nowUtc)
    {
        if (ReadDurationMinutes(root) is null ||
            !root.TryGetProperty("currentPeriod", out var period) ||
            period.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("isUnifiedBillingUser", out var unified) ||
            unified.ValueKind != JsonValueKind.True ||
            !TryGetCent(root, "onDemandCap", out var onDemandCap) ||
            onDemandCap != 0 ||
            !TryGetCent(root, "onDemandUsed", out var onDemandUsed) ||
            onDemandUsed != 0)
        {
            return false;
        }

        if (!TryReadUtc(ReadString(period, "start"), out var startUtc) ||
            !TryReadUtc(ReadString(period, "end"), out var endUtc) ||
            !TryReadUtc(ReadString(root, "billingPeriodStart"), out var billingStartUtc) ||
            !TryReadUtc(ReadString(root, "billingPeriodEnd"), out var billingEndUtc))
        {
            return false;
        }

        return startUtc == billingStartUtc &&
               endUtc == billingEndUtc &&
               startUtc <= nowUtc &&
               nowUtc < endUtc;
    }

    private static bool TryReadUtc(string? value, out DateTimeOffset parsed)
    {
        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out parsed);
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
        return el.TryGetProperty(name, out var p) &&
               p.ValueKind == JsonValueKind.Number &&
               p.TryGetDouble(out value) &&
               double.IsFinite(value);
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

            if (val.ValueKind == JsonValueKind.String &&
                long.TryParse(val.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value))
            {
                return true;
            }
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
