using System.Text.RegularExpressions;

namespace BuildnBits.Usage.Core.Parsing;

public static class UsageLabel
{
    public static string Display(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return "";
        }

        var s = raw.Trim();
        if (s.Equals("5-hour", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("5h", StringComparison.OrdinalIgnoreCase))
        {
            return "5h";
        }

        if (s.Equals("7-day", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("7d", StringComparison.OrdinalIgnoreCase))
        {
            return "7d";
        }

        if (s.Equals("Weekly", StringComparison.OrdinalIgnoreCase) ||
            s.Equals("Build", StringComparison.OrdinalIgnoreCase))
        {
            return "Build";
        }

        if (s.Equals("Bot", StringComparison.OrdinalIgnoreCase))
        {
            return "Bot";
        }

        s = Regex.Replace(s, @"\s*Limit Remaining\s*", " ", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bModels\b", "", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"Claude and GPT", "Claude", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"Five Hour|\b5-hour\b", "5h", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\bWeekly\b", "7d", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\b7-day\b", "7d", RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\s*·\s*", " ");
        s = Regex.Replace(s, @"\s+", " ").Trim();
        return s;
    }
}
