using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;
using BuildnBits.Usage.Tray.Icons;

namespace BuildnBits.Usage.Tests;

public class IconAndShellTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    [InlineData(42)]
    [InlineData(100)]
    public void Renders_exact_percent_icons(int remaining)
    {
        using var icon = UsageIconRenderer.Create(ProviderKind.Codex, remaining, highContrast: false, largerDigits: true);
        Assert.NotNull(icon);
        Assert.True(icon.Width >= 32);
        Assert.Equal(icon.Width, icon.Height);
        using var grok = UsageIconRenderer.Create(ProviderKind.Grok, remaining, highContrast: true, largerDigits: true);
        Assert.NotNull(grok);
        Assert.Equal(remaining, PercentageMath.DisplayPercent(remaining));
        using var agy = UsageIconRenderer.Create(ProviderKind.Agy, remaining, highContrast: false, largerDigits: true);
        Assert.NotNull(agy);
        Assert.Equal(agy.Width, agy.Height);
    }

    [Fact]
    public void Notify_icon_host_uses_supported_shell_surface()
    {
        var host = File.ReadAllText(FindSource("BuildnBits.Usage.Tray", "Icons", "NotifyIconHost.cs"));
        var watcher = File.ReadAllText(FindSource("BuildnBits.Usage.Tray", "Icons", "TaskbarCreatedWindow.cs"));
        Assert.Contains("Shell_NotifyIcon", host);
        Assert.DoesNotContain("SetParent", host);
        Assert.Contains("TaskbarCreated", watcher);
    }

    [Theory]
    [InlineData(96)]
    [InlineData(144)]
    [InlineData(192)]
    public void Renders_at_mixed_dpi(int dpi)
    {
        using var icon = UsageIconRenderer.Create(ProviderKind.Codex, 100, highContrast: false, dpiOverride: dpi, largerDigits: true);
        Assert.NotNull(icon);
        Assert.True(icon.Width >= 32);
        Assert.Equal(icon.Width, icon.Height);
    }

    [Fact]
    public void Notify_icon_host_uses_period_selection_without_adding_shell_slots()
    {
        var host = File.ReadAllText(FindSource("BuildnBits.Usage.Tray", "Icons", "NotifyIconHost.cs"));

        Assert.Contains("TraySquareCatalog.SelectedOptions", host, StringComparison.Ordinal);
        Assert.Contains("TraySquareCatalog.SelectedWindows", host, StringComparison.Ordinal);
        Assert.Equal(3, host.Split("private readonly NotifyIcon ", StringSplitOptions.None).Length - 1);
    }

    [Fact]
    public void Grok_popup_renders_every_usage_window()
    {
        var popup = File.ReadAllText(FindSource("BuildnBits.Usage.Tray", "Ui", "UsagePopupForm.cs"));

        Assert.Contains("foreach (var window in OrderedWindows(snapshot.Windows)", popup, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot.Weekly ?? snapshot.Windows.FirstOrDefault()", popup, StringComparison.Ordinal);
        Assert.Equal(3, popup.Split("OrderedWindows(snapshot.Windows)", StringSplitOptions.None).Length - 1);
    }

    private static string FindSource(string project, params string[] parts)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine([dir.FullName, "src", project, .. parts]);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            dir = dir.Parent;
        }

        throw new FileNotFoundException(Path.Combine([project, .. parts]));
    }
}
