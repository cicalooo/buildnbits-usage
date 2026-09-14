using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Storage;
using BuildnBits.Usage.Tray.Icons;

namespace BuildnBits.Usage.Tests;

public sealed class TraySquareSettingsTests
{
    [Fact]
    public void Catalog_exposes_known_periods_and_groups_same_period_pools()
    {
        var state = State(
            codex: [
                new UsageWindow("5-hour", 300, 20, 80, DateTimeOffset.UtcNow.AddHours(1)),
                new UsageWindow("7-day", 10080, 40, 60, DateTimeOffset.UtcNow.AddDays(2))
            ],
            grok: [
                new UsageWindow("Build", 10080, 10, 90, DateTimeOffset.UtcNow.AddDays(3)),
                new UsageWindow("Bot", 10080, 30, 70, DateTimeOffset.UtcNow.AddDays(3))
            ],
            agy: [
                new UsageWindow("Gemini", 10080, 25, 75, DateTimeOffset.UtcNow.AddDays(4)),
                new UsageWindow("Claude", 10080, 50, 50, DateTimeOffset.UtcNow.AddDays(4))
            ]);

        var options = TraySquareCatalog.Build(state);

        Assert.Contains(options, option =>
            option.Key == TraySquareKeys.CodexFiveHour && option.DisplayLabel == "Codex 5-hour");
        Assert.Contains(options, option =>
            option.Key == TraySquareKeys.CodexSevenDay && option.DisplayLabel == "Codex 7-day");
        var grok = Assert.Single(options, option => option.Provider == ProviderKind.Grok);
        var grokBuild = Assert.Single(grok.Windows);
        Assert.Equal(TraySquareKeys.GrokWeekly, grok.Key);
        Assert.Equal("Build", grokBuild.Label);
        Assert.Equal(2, options.Single(option => option.Key == TraySquareKeys.AgyWeekly).Windows.Count);
    }

    [Fact]
    public void Selected_periods_only_feed_the_provider_square()
    {
        var state = State(
            codex: [
                new UsageWindow("5-hour", 300, 20, 80, null),
                new UsageWindow("7-day", 10080, 40, 60, null)
            ],
            grok: [new UsageWindow("Weekly", 10080, 10, 90, null)],
            agy: []);
        var settings = new AppSettings();
        settings.SetTraySquareVisible(TraySquareKeys.CodexFiveHour, false);
        settings.SetTraySquareVisible(TraySquareKeys.CodexSevenDay, true);

        var selected = TraySquareCatalog.SelectedWindows(state, ProviderKind.Codex, settings);

        var window = Assert.Single(selected);
        Assert.Equal("7-day", window.Label);
        Assert.Equal(60, window.RemainingPercent);
    }

    [Fact]
    public void Explicit_period_visibility_roundtrips_without_secrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-tray-squares-" + Guid.NewGuid());
        var store = new AppSettingsStore(dir);
        var settings = new AppSettings();
        settings.SetTraySquareVisible(TraySquareKeys.CodexFiveHour, false);
        settings.SetTraySquareVisible(TraySquareKeys.CodexSevenDay, true);

        store.Save(settings);
        var loaded = store.Load();
        var json = File.ReadAllText(store.PathOnDisk);

        Assert.False(loaded.IsTraySquareVisible(TraySquareKeys.CodexFiveHour, ProviderKind.Codex));
        Assert.True(loaded.IsTraySquareVisible(TraySquareKeys.CodexSevenDay, ProviderKind.Codex));
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unknown_visible_key_does_not_overwrite_explicit_false_known_period()
    {
        var settings = new AppSettings
        {
            ShowCodexIcon = false,
            ShowGrokIcon = false,
            ShowAgyIcon = false,
            TraySquareVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
            {
                [TraySquareKeys.CodexFiveHour] = false,
                [TraySquareKeys.CodexSevenDay] = false,
                [TraySquareKeys.GrokWeekly] = false,
                [TraySquareKeys.AgyWeekly] = false,
                ["codex:duration:15"] = true
            }
        };
        settings.Normalize();

        TraySquareCatalog.EnsureAtLeastOneVisible(State([], [], []), settings);

        Assert.False(settings.IsTraySquareVisible(TraySquareKeys.CodexFiveHour, ProviderKind.Codex));
        Assert.True(settings.TraySquareVisibility["codex:duration:15"]);
    }

    private static CombinedUsageState State(
        IReadOnlyList<UsageWindow> codex,
        IReadOnlyList<UsageWindow> grok,
        IReadOnlyList<UsageWindow> agy)
    {
        var now = DateTimeOffset.UtcNow;
        return new CombinedUsageState(
            new ProviderSnapshot(ProviderKind.Codex, UsageStatus.Ok, null, codex, now, null),
            new ProviderSnapshot(ProviderKind.Grok, UsageStatus.Ok, null, grok, now, null),
            new ProviderSnapshot(ProviderKind.Agy, UsageStatus.Ok, null, agy, now, null),
            now,
            now);
    }
}
