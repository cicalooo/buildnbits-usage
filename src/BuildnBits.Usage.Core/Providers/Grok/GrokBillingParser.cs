using System.Globalization;
using System.Text.Json;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;

namespace BuildnBits.Usage.Core.Providers.Grok;

public static class GrokBillingParser
{
    private const int WeeklyPeriodType = 2;

    public static ProviderSnapshot Parse(JsonElement result, DateTimeOffset nowUtc)
    {
        var root = result;
        if (result.TryGetProperty("config", out var nested) && nested.ValueKind == JsonValueKind.Object)
        {
            root = nested;
        }

        var resets = ReadReset(root);
        var durationMinutes = ReadDurationMinutes(root);
        var productWindows = ReadProductUsage(root, durationMinutes, resets);
        IReadOnlyList<UsageWindow> windows = productWindows ?? [];
        if (productWindows is null)
        {
            var usedRaw = ReadUsedPercent(root);
            var used = PercentageMath.ClampPercent(usedRaw);
            var remaining = PercentageMath.RemainingFromUsed(usedRaw);
            windows = [new UsageWindow("Build", durationMinutes, used, remaining, resets)];
        }

        var plan = ReadString(result, "subscriptionTier") ?? ReadString(root, "subscriptionTier");
        return new ProviderSnapshot(ProviderKind.Grok, UsageStatus.Ok, plan, windows, nowUtc, null);
    }

    private static int? ReadDurationMinutes(JsonElement root)
    {
        if (root.TryGetProperty("currentPeriod", out var period) && period.ValueKind == JsonValueKind.Object)
        {
            if (ReadString(period, "type") is { } type &&
                type.Contains("WEEK", StringComparison.OrdinalIgnoreCase))
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

    private static IReadOnlyList<UsageWindow>? ReadProductUsage(
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

        var windows = new List<UsageWindow>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var product in products.EnumerateArray())
        {
            if (product.ValueKind != JsonValueKind.Object ||
                !TryReadProductLabel(product, out var label) ||
                !TryGetDouble(product, "usagePercent", out var usedRaw) ||
                !seen.Add(label))
            {
                continue;
            }

            var used = PercentageMath.ClampPercent(usedRaw);
            windows.Add(new UsageWindow(
                label,
                durationMinutes,
                used,
                PercentageMath.RemainingFromUsed(usedRaw),
                resetsAtUtc));
        }

        return windows
            .OrderBy(window => string.Equals(window.Label, "Build", StringComparison.Ordinal) ? 0 : 1)
            .ToArray();
    }

    private static bool TryReadProductLabel(JsonElement product, out string label)
    {
        label = string.Empty;
        if (!product.TryGetProperty("product", out var value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var productId))
        {
            label = productId switch
            {
                2 => "Build",
                4 => "Bot",
                _ => string.Empty
            };
            return label.Length > 0;
        }

        if (value.ValueKind != JsonValueKind.String || value.GetString() is not { } raw)
        {
            return false;
        }

        var name = raw.Trim().ToUpperInvariant();
        if (name.StartsWith("PRODUCT_", StringComparison.Ordinal))
        {
            name = name["PRODUCT_".Length..];
        }

        label = name switch
        {
            "GROK_BUILD" or "BUILD" => "Build",
            "CHAT" or "GROK_BOT" or "BOT" => "Bot",
            _ => string.Empty
        };
        return label.Length > 0;
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
