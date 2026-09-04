using System.Globalization;
using System.Text.Json;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;

namespace BuildnBits.Usage.Core.Providers.Agy;

public static class AgyUsageParser
{
    public const int WeeklyWindowMinutes = 10080;

    public static ProviderSnapshot Parse(string json, DateTimeOffset nowUtc)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            throw new InvalidOperationException("Antigravity usage payload is empty.");
        }

        using var document = JsonDocument.Parse(json);
        return Parse(document.RootElement, nowUtc);
    }

    public static ProviderSnapshot Parse(JsonElement result, DateTimeOffset nowUtc)
    {
        var status = ReadString(result, "status");
        if (status is not null && !string.Equals(status, "SUCCESS", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                ReadError(result) ?? "Antigravity usage command failed.");
        }

        var data = result;
        if (TryGetObject(result, "command", out var command))
        {
            data = command;
            if (TryGetObject(command, "data", out var commandData))
            {
                data = commandData;
            }
        }
        else if (TryGetObject(result, "data", out var nestedData))
        {
            data = nestedData;
        }

        if (!data.TryGetProperty("groups", out var groups) || groups.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidOperationException("Antigravity usage payload is missing quota groups.");
        }

        var windows = new List<UsageWindow>();
        foreach (var group in groups.EnumerateArray())
        {
            if (group.ValueKind != JsonValueKind.Object ||
                !group.TryGetProperty("buckets", out var buckets) ||
                buckets.ValueKind != JsonValueKind.Array)
            {
                continue;
            }

            var groupName = ReadString(group, "name") ?? "Model quota";
            foreach (var bucket in buckets.EnumerateArray())
            {
                if (bucket.ValueKind != JsonValueKind.Object ||
                    !TryReadUsage(bucket, out var used, out var remaining))
                {
                    continue;
                }

                var bucketName = ReadString(bucket, "name") ??
                                 ReadString(bucket, "id") ??
                                 "Usage";
                var label = string.Equals(groupName, bucketName, StringComparison.OrdinalIgnoreCase)
                    ? groupName
                    : $"{groupName} · {bucketName}";
                var window = ReadString(bucket, "window");
                int? duration = IsWeekly(window) || IsWeekly(bucketName)
                    ? WeeklyWindowMinutes
                    : null;

                windows.Add(new UsageWindow(
                    label,
                    duration,
                    used,
                    remaining,
                    ReadReset(bucket, nowUtc)));
            }
        }

        if (windows.Count == 0)
        {
            throw new InvalidOperationException("Antigravity usage payload contains no quota buckets.");
        }

        var plan = ReadString(data, "plan_tier") ??
                   ReadString(data, "planTier") ??
                   ReadString(result, "plan_tier") ??
                   ReadString(result, "planTier");
        return new ProviderSnapshot(ProviderKind.Agy, UsageStatus.Ok, plan, windows, nowUtc, null);
    }

    private static bool TryReadUsage(JsonElement bucket, out double used, out double remaining)
    {
        used = 0;
        remaining = 0;

        if (TryGetNumber(bucket, "remaining_fraction", out var remainingFraction) ||
            TryGetNumber(bucket, "remainingFraction", out remainingFraction))
        {
            if (!double.IsFinite(remainingFraction))
            {
                return false;
            }

            remaining = PercentageMath.ClampPercent(remainingFraction <= 1
                ? remainingFraction * 100
                : remainingFraction);
            used = PercentageMath.RemainingFromUsed(remaining);
            return true;
        }

        if (TryGetNumber(bucket, "remaining_percent", out var remainingPercent) ||
            TryGetNumber(bucket, "remainingPercent", out remainingPercent))
        {
            if (!double.IsFinite(remainingPercent))
            {
                return false;
            }

            remaining = PercentageMath.ClampPercent(remainingPercent);
            used = PercentageMath.RemainingFromUsed(remaining);
            return true;
        }

        if (TryGetNumber(bucket, "used_fraction", out var usedFraction) ||
            TryGetNumber(bucket, "usedFraction", out usedFraction))
        {
            if (!double.IsFinite(usedFraction))
            {
                return false;
            }

            used = PercentageMath.ClampPercent(usedFraction <= 1
                ? usedFraction * 100
                : usedFraction);
            remaining = PercentageMath.RemainingFromUsed(used);
            return true;
        }

        if (TryGetNumber(bucket, "used_percent", out var usedPercent) ||
            TryGetNumber(bucket, "usedPercent", out usedPercent))
        {
            if (!double.IsFinite(usedPercent))
            {
                return false;
            }

            used = PercentageMath.ClampPercent(usedPercent);
            remaining = PercentageMath.RemainingFromUsed(used);
            return true;
        }

        return false;
    }

    private static DateTimeOffset? ReadReset(JsonElement bucket, DateTimeOffset nowUtc)
    {
        var raw = ReadString(bucket, "reset_time") ??
                  ReadString(bucket, "resetTime") ??
                  ReadString(bucket, "resetAt");
        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return parsed;
        }

        if (TryGetNumber(bucket, "reset_in_seconds", out var seconds) && double.IsFinite(seconds))
        {
            return nowUtc.AddSeconds(Math.Max(0, seconds));
        }

        if (TryGetNumber(bucket, "resetInSeconds", out seconds) && double.IsFinite(seconds))
        {
            return nowUtc.AddSeconds(Math.Max(0, seconds));
        }

        return null;
    }

    private static bool IsWeekly(string? value) =>
        value?.Contains("week", StringComparison.OrdinalIgnoreCase) == true;

    private static bool TryGetObject(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out value) &&
            value.ValueKind == JsonValueKind.Object)
        {
            return true;
        }

        value = default;
        return false;
    }

    private static bool TryGetNumber(JsonElement element, string name, out double value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Object &&
               element.TryGetProperty(name, out var property) &&
               property.ValueKind == JsonValueKind.Number &&
               property.TryGetDouble(out value);
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(name, out var property) &&
            property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return null;
    }

    private static string? ReadError(JsonElement element)
    {
        if (!element.TryGetProperty("error", out var error))
        {
            return null;
        }

        if (error.ValueKind == JsonValueKind.String)
        {
            return error.GetString();
        }

        return error.ValueKind == JsonValueKind.Object
            ? ReadString(error, "message")
            : null;
    }
}

// Keep the product name available to callers that prefer the long form.
public static class AntigravityUsageParser
{
    public static ProviderSnapshot Parse(string json, DateTimeOffset nowUtc) =>
        AgyUsageParser.Parse(json, nowUtc);

    public static ProviderSnapshot Parse(JsonElement result, DateTimeOffset nowUtc) =>
        AgyUsageParser.Parse(result, nowUtc);
}
