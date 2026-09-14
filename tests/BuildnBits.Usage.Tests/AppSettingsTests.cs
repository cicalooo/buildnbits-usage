using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Portable_root_uses_data_folder_when_marker_present()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-port-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, StoragePaths.PortableMarkerFileName), "portable");
        // ResolveRoot with explicit path still wins.
        var explicitRoot = Path.Combine(dir, "explicit");
        var resolved = StoragePaths.ResolveRoot(explicitRoot);
        Assert.Equal(Path.GetFullPath(explicitRoot), Path.GetFullPath(resolved));
        var cache = new UsageCache(resolved);
        cache.Save(BuildnBits.Usage.Core.Models.CombinedUsageState.Empty);
        Assert.StartsWith(resolved, cache.PathOnDisk);
    }

    [Fact]
    public void Roundtrips_and_keeps_at_least_one_icon()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-settings-" + Guid.NewGuid());
        var store = new AppSettingsStore(dir);
        store.Save(new AppSettings
        {
            ShowCodexIcon = false,
            ShowGrokIcon = false,
            ShowAgyIcon = false,
            LargerTrayDigits = false
        });
        var loaded = store.Load();
        Assert.True(loaded.ShowCodexIcon);
        Assert.False(loaded.LargerTrayDigits);
        Assert.DoesNotContain("token", File.ReadAllText(store.PathOnDisk), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Legacy_provider_flags_seed_period_visibility_on_save()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-settings-legacy-" + Guid.NewGuid());
        var store = new AppSettingsStore(dir);
        File.WriteAllText(
            store.PathOnDisk,
            """{"showCodexIcon":false,"showGrokIcon":true,"showAgyIcon":false}""");

        var loaded = store.Load();
        store.Save(loaded);
        var json = File.ReadAllText(store.PathOnDisk);

        Assert.False(loaded.IsTraySquareVisible(TraySquareKeys.CodexFiveHour, ProviderKind.Codex));
        Assert.True(loaded.IsTraySquareVisible(TraySquareKeys.GrokWeekly, ProviderKind.Grok));
        Assert.Contains("traySquareVisibility", json, StringComparison.Ordinal);
        Assert.Contains("codex:300", json, StringComparison.Ordinal);
    }

    [Fact]
    public void Partial_visibility_map_does_not_fall_back_to_mutated_codex_flag()
    {
        var settings = new AppSettings
        {
            ShowCodexIcon = false,
            ShowGrokIcon = false,
            ShowAgyIcon = false
        };
        settings.SetTraySquareVisible(TraySquareKeys.GrokWeekly, true);

        settings.Normalize();

        Assert.False(settings.IsTraySquareVisible(TraySquareKeys.CodexFiveHour, ProviderKind.Codex));
        Assert.True(settings.IsTraySquareVisible(TraySquareKeys.GrokWeekly, ProviderKind.Grok));
        Assert.False(settings.IsTraySquareVisible(TraySquareKeys.AgyWeekly, ProviderKind.Agy));
    }

    [Fact]
    public void Explicit_false_period_override_survives_when_legacy_provider_is_on()
    {
        var settings = new AppSettings
        {
            ShowCodexIcon = true,
            ShowGrokIcon = false,
            ShowAgyIcon = false
        };
        settings.SetTraySquareVisible(TraySquareKeys.CodexFiveHour, false);

        settings.Normalize();

        Assert.False(settings.IsTraySquareVisible(TraySquareKeys.CodexFiveHour, ProviderKind.Codex));
        Assert.True(settings.IsTraySquareVisible(TraySquareKeys.CodexSevenDay, ProviderKind.Codex));
    }

    [Fact]
    public void Extensible_true_period_keeps_explicit_false_known_period()
    {
        var settings = new AppSettings
        {
            ShowCodexIcon = false,
            ShowGrokIcon = false,
            ShowAgyIcon = false
        };
        settings.SetTraySquareVisible(TraySquareKeys.CodexFiveHour, false);
        settings.SetTraySquareVisible("codex:duration:15", true);

        settings.Normalize();

        Assert.False(settings.IsTraySquareVisible(TraySquareKeys.CodexFiveHour, ProviderKind.Codex));
        Assert.True(settings.TraySquareVisibility["codex:duration:15"]);
    }

}