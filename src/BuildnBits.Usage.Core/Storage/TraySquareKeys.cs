using BuildnBits.Usage.Core.Models;

namespace BuildnBits.Usage.Core.Storage;

public static class TraySquareKeys
{
    public const string CodexFiveHour = "codex:300";
    public const string CodexSevenDay = "codex:10080";
    public const string GrokWeekly = "grok:weekly";
    public const string AgyWeekly = "agy:10080";

    public static string For(ProviderKind provider, UsageWindow window)
    {
        if (provider == ProviderKind.Grok && (IsWeekly(window) || window.DurationMinutes is null))
        {
            return GrokWeekly;
        }

        if (provider == ProviderKind.Agy && IsWeekly(window))
        {
            return AgyWeekly;
        }

        if (provider == ProviderKind.Codex && window.DurationMinutes == 300)
        {
            return CodexFiveHour;
        }

        if (provider == ProviderKind.Codex && window.DurationMinutes == 10080)
        {
            return CodexSevenDay;
        }

        var prefix = provider switch
        {
            ProviderKind.Codex => "codex",
            ProviderKind.Grok => "grok",
            ProviderKind.Agy => "agy",
            _ => provider.ToString().ToLowerInvariant()
        };
        return window.DurationMinutes is { } duration
            ? $"{prefix}:duration:{duration}"
            : $"{prefix}:label:{NormalizeLabel(window.Label)}";
    }

    public static bool IsWeekly(UsageWindow window) =>
        window.DurationMinutes == 10080 ||
        window.Label?.Contains("week", StringComparison.OrdinalIgnoreCase) == true;

    private static string NormalizeLabel(string? value)
    {
        var result = new System.Text.StringBuilder();
        foreach (var character in (value ?? string.Empty).Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(character))
            {
                result.Append(character);
            }
            else if (result.Length > 0 && result[^1] != '-')
            {
                result.Append('-');
            }
        }

        if (result.Length > 0 && result[^1] == '-')
        {
            result.Length--;
        }

        return result.Length == 0 ? "unknown" : result.ToString();
    }
}
