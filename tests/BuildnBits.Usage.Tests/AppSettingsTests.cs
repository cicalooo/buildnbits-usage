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
}
