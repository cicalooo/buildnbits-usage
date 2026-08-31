using System.Globalization;
using System.Text.Json;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;

namespace BuildnBits.Usage.Core.Providers.Grok;

public static class GrokBillingParser
{
    public static ProviderSnapshot Parse(JsonElement result, DateTimeOffset nowUtc)
    {
        var root = result;
        if (result.TryGetProperty("config", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            root = nested;
        }

        var usedRaw = ReadUsedPercent(root);
        var remaining = PercentageMath.RemainingFromUsed(usedRaw);
        var used = PercentageMath.ClampPercent(usedRaw);
        var resets = ReadReset(root);
        var plan = ReadString(result, "subscriptionTier") ?? ReadString(root, "subscriptionTier");

        var window = new UsageWindow("Weekly", null, used, remaining, resets);
        return new ProviderSnapshot(ProviderKind.Grok, UsageStatus.Ok, plan, [window], nowUtc, null);
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
