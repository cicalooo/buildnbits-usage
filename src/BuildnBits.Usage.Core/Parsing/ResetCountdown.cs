namespace BuildnBits.Usage.Core.Parsing;

public static class ResetCountdown
{
    public static TimeSpan? Remaining(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc)
    {
        if (resetsAtUtc is null)
        {
            return null;
        }

        var delta = resetsAtUtc.Value - nowUtc;
        return delta < TimeSpan.Zero ? TimeSpan.Zero : delta;
    }

    public static string Format(TimeSpan? remaining)
    {
        if (remaining is null)
        {
            return "reset unknown";
        }

        var t = remaining.Value;
        if (t <= TimeSpan.Zero)
        {
            return "resets now";
        }

        if (t.TotalDays >= 1)
        {
            return $"{(int)t.TotalDays}d {t.Hours}h remaining";
        }

        if (t.TotalHours >= 1)
        {
            return $"{(int)t.TotalHours}h {t.Minutes}m remaining";
        }

        return $"{t.Minutes}m {t.Seconds}s remaining";
    }

    public static string LocalResetLabel(DateTimeOffset? resetsAtUtc)
    {
        if (resetsAtUtc is null)
        {
            return "reset time unavailable";
        }

        var local = resetsAtUtc.Value.ToLocalTime();
        return $"resets {local:ddd HH:mm}";
    }
}
