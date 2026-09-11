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

    public static string FormatCompact(TimeSpan? remaining)
    {
        if (remaining is null)
        {
            return "—";
        }

        var t = remaining.Value;
        if (t <= TimeSpan.Zero)
        {
            return "now";
        }

        if (t.TotalDays >= 1)
        {
            var days = (int)t.TotalDays;
            return t.Hours == 0 ? $"{days}d" : $"{days}d {t.Hours}h";
        }

        if (t.TotalHours >= 1)
        {
            var hours = (int)t.TotalHours;
            return t.Minutes == 0 ? $"{hours}h" : $"{hours}h {t.Minutes}m";
        }

        return $"{t.Minutes}m";
    }

    public static string Caption(DateTimeOffset? resetsAtUtc, DateTimeOffset nowUtc)
    {
        var compact = FormatCompact(Remaining(resetsAtUtc, nowUtc));
        if (resetsAtUtc is null)
        {
            return compact;
        }

        return $"{compact} · {resetsAtUtc.Value.ToLocalTime():ddd HH:mm}";
    }

    // Adaptive Card + any leftover callers.
    public static string Format(TimeSpan? remaining) => FormatCompact(remaining);

    public static string LocalResetLabel(DateTimeOffset? resetsAtUtc) =>
        resetsAtUtc is null ? "—" : resetsAtUtc.Value.ToLocalTime().ToString("ddd HH:mm");
}
