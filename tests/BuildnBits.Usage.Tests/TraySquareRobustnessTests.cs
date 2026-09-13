using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Storage;
using BuildnBits.Usage.Tray.Icons;

namespace BuildnBits.Usage.Tests;

public sealed class TraySquareRobustnessTests
{
    [Fact]
    public void Null_tray_square_visibility_loads_with_default_periods()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-settings-null-" + Guid.NewGuid());
        var store = new AppSettingsStore(dir);
        File.WriteAllText(store.PathOnDisk, """{"traySquareVisibility":null}""");

        var loaded = store.Load();

        Assert.True(loaded.IsTraySquareVisible(TraySquareKeys.CodexFiveHour, ProviderKind.Codex));
        Assert.True(loaded.IsTraySquareVisible(TraySquareKeys.GrokWeekly, ProviderKind.Grok));
    }

    [Fact]
    public void Null_cached_window_label_is_normalized_before_catalog_build()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-cache-null-label-" + Guid.NewGuid());
        var cache = new UsageCache(dir);
        File.WriteAllText(
            cache.PathOnDisk,
            """{"agy":{"status":"Ok","windows":[{"label":null,"durationMinutes":null,"usedPercent":10,"remainingPercent":90}]}}""");

        var state = cache.Load();
        var options = TraySquareCatalog.Build(state);

        Assert.Equal("Usage", state.Agy.Windows.Single().Label);
        Assert.Contains(options, option => option.Provider == ProviderKind.Agy);
    }
}
