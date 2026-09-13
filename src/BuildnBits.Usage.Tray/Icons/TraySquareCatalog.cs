using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Tray.Icons;

public sealed record TraySquareOption(
    string Key,
    ProviderKind Provider,
    string DisplayLabel,
    IReadOnlyList<UsageWindow> Windows);

public static class TraySquareCatalog
{
    public static IReadOnlyList<TraySquareOption> Build(CombinedUsageState state)
    {
        var options = new List<TraySquareOption>();
        options.AddRange(BuildCodex(state.Codex));
        options.AddRange(BuildGrok(state.Grok));
        options.AddRange(BuildAgy(state.Agy));
        return options;
    }

    public static IReadOnlyList<TraySquareOption> SelectedOptions(
        CombinedUsageState state,
        ProviderKind provider,
        AppSettings settings)
    {
        return Build(state)
            .Where(option => option.Provider == provider)
            .Where(option => settings.IsTraySquareVisible(option.Key, option.Provider))
            .ToArray();
    }

    public static IReadOnlyList<UsageWindow> SelectedWindows(
        CombinedUsageState state,
        ProviderKind provider,
        AppSettings settings)
    {
        return SelectedOptions(state, provider, settings)
            .SelectMany(option => option.Windows)
            .ToArray();
    }

    public static void EnsureAtLeastOneVisible(CombinedUsageState state, AppSettings settings)
    {
        var options = Build(state);
        if (options.Count > 0 && !options.Any(option =>
                settings.IsTraySquareVisible(option.Key, option.Provider)))
        {
            settings.SetTraySquareVisible(options[0].Key, true);
        }
    }

    private static IReadOnlyList<TraySquareOption> BuildCodex(ProviderSnapshot snapshot)
    {
        var remaining = snapshot.Windows.ToList();
        var options = new List<TraySquareOption>();
        AddKnown(
            options,
            remaining,
            ProviderKind.Codex,
            TraySquareKeys.CodexFiveHour,
            "Codex 5-hour",
            window => window.DurationMinutes == 300);
        AddKnown(
            options,
            remaining,
            ProviderKind.Codex,
            TraySquareKeys.CodexSevenDay,
            "Codex 7-day",
            window => window.DurationMinutes == 10080);
        AddUnknown(options, remaining, ProviderKind.Codex);
        return options;
    }

    private static IReadOnlyList<TraySquareOption> BuildGrok(ProviderSnapshot snapshot)
    {
        var remaining = snapshot.Windows.ToList();
        var options = new List<TraySquareOption>();
        AddKnown(
            options,
            remaining,
            ProviderKind.Grok,
            TraySquareKeys.GrokWeekly,
            "Grok weekly",
            window => TraySquareKeys.IsWeekly(window) || window.DurationMinutes is null);
        AddUnknown(options, remaining, ProviderKind.Grok);
        return options;
    }

    private static IReadOnlyList<TraySquareOption> BuildAgy(ProviderSnapshot snapshot)
    {
        var remaining = snapshot.Windows.ToList();
        var options = new List<TraySquareOption>();
        AddKnown(
            options,
            remaining,
            ProviderKind.Agy,
            TraySquareKeys.AgyWeekly,
            "Antigravity weekly",
            TraySquareKeys.IsWeekly);
        AddUnknown(options, remaining, ProviderKind.Agy);
        return options;
    }

    private static void AddKnown(
        ICollection<TraySquareOption> options,
        List<UsageWindow> remaining,
        ProviderKind provider,
        string key,
        string displayLabel,
        Predicate<UsageWindow> matches)
    {
        var matching = remaining.Where(window => matches(window)).ToArray();
        remaining.RemoveAll(matches);
        options.Add(new TraySquareOption(key, provider, displayLabel, matching));
    }

    private static void AddUnknown(
        ICollection<TraySquareOption> options,
        IEnumerable<UsageWindow> remaining,
        ProviderKind provider)
    {
        foreach (var group in remaining.GroupBy(window => TraySquareKeys.For(provider, window)))
        {
            var windows = group.ToArray();
            var period = windows[0].DurationMinutes is { } duration
                ? $"{duration}-minute"
                : windows[0].Label;
            options.Add(new TraySquareOption(
                group.Key,
                provider,
                $"{ProviderLabel(provider)} {period}",
                windows));
        }
    }

    private static string ProviderLabel(ProviderKind provider) => provider switch
    {
        ProviderKind.Codex => "Codex",
        ProviderKind.Grok => "Grok",
        _ => "Antigravity"
    };
}
