using System.Windows.Forms;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;
using BuildnBits.Usage.Core.Storage;
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

    [Theory]
    [InlineData(10)]
    [InlineData(88)]
    [InlineData(92)]
    [InlineData(100)]
    public void Larger_digits_keep_ink_inside_the_border(int remaining)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var icon = UsageIconRenderer.Create(
            ProviderKind.Codex,
            remaining,
            highContrast: false,
            dpiOverride: 96,
            largerDigits: true);
        using var bitmap = icon.ToBitmap();
        Assert.True(bitmap.Width >= 32);

        var inkOnOuterRing = false;
        var inkOnBottomPadding = false;
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = bitmap.GetPixel(x, y);
                var isWhiteInk = pixel.A > 200 && pixel.R > 220 && pixel.G > 220 && pixel.B > 220;
                if (!isWhiteInk)
                {
                    continue;
                }

                var onOuterRing = x == 0 || y == 0 || x == bitmap.Width - 1 || y == bitmap.Height - 1;
                if (onOuterRing)
                {
                    inkOnOuterRing = true;
                }

                // Keep the underside of the glyph clear of the bottom border.
                if (y >= bitmap.Height - 5)
                {
                    inkOnBottomPadding = true;
                }
            }
        }

        Assert.False(inkOnOuterRing, $"Digit ink touched the outer border for {remaining}%.");
        Assert.False(inkOnBottomPadding, $"Digit ink sat too low in the square for {remaining}%.");
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
    public void Notify_icon_host_creates_one_shell_icon_per_selected_option()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Exception? error = null;
        var visibleCount = -1;
        var keys = string.Empty;
        var thread = new Thread(() =>
        {
            try
            {
                var now = DateTimeOffset.UtcNow;
                var agyFiveHour = new UsageWindow(
                    "Gemini Models · Five Hour Limit Remaining",
                    300,
                    20,
                    80,
                    now.AddHours(1));
                var state = new CombinedUsageState(
                    new ProviderSnapshot(
                        ProviderKind.Codex,
                        UsageStatus.Ok,
                        "Plus",
                        [
                            new UsageWindow("5-hour", 300, 20, 80, now.AddHours(1)),
                            new UsageWindow("7-day", 10080, 40, 60, now.AddDays(2))
                        ],
                        now,
                        null),
                    new ProviderSnapshot(
                        ProviderKind.Grok,
                        UsageStatus.Ok,
                        "SuperGrok",
                        [
                            new UsageWindow("Build", 10080, 10, 90, now.AddDays(3)),
                            new UsageWindow("Bot", 10080, 25, 75, now.AddDays(3))
                        ],
                        now,
                        null),
                    new ProviderSnapshot(
                        ProviderKind.Agy,
                        UsageStatus.Ok,
                        "Standard",
                        [
                            new UsageWindow("Gemini Models · Weekly Limit Remaining", 10080, 50, 50, now.AddDays(4)),
                            agyFiveHour
                        ],
                        now,
                        null),
                    now,
                    now);
                var settings = new AppSettings
                {
                    TraySquareVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        [TraySquareKeys.CodexFiveHour] = true,
                        [TraySquareKeys.CodexSevenDay] = true,
                        [TraySquareKeys.GrokWeekly] = true,
                        [TraySquareKeys.AgyWeekly] = true,
                        [TraySquareKeys.For(ProviderKind.Agy, agyFiveHour)] = true
                    }
                };

                using var host = new NotifyIconHost();
                host.Apply(state, launchAtLogin: false, settings);
                var field = typeof(NotifyIconHost).GetField(
                    "_icons",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var icons = (System.Collections.IDictionary?)field?.GetValue(host);
                Assert.NotNull(icons);
                visibleCount = icons!.Values.Cast<NotifyIcon>().Count(icon => icon.Visible);
                keys = string.Join("|", icons.Keys.Cast<string>().OrderBy(key => key, StringComparer.Ordinal));
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Equal(5, visibleCount);
        Assert.Contains(TraySquareKeys.CodexFiveHour, keys);
        Assert.Contains(TraySquareKeys.CodexSevenDay, keys);
        Assert.Contains(TraySquareKeys.GrokWeekly, keys);
        Assert.Contains(TraySquareKeys.AgyWeekly, keys);
    }

    [Fact]
    public void Grok_tray_catalog_exposes_only_the_build_square()
    {
        var now = DateTimeOffset.UtcNow;
        var state = new CombinedUsageState(
            new ProviderSnapshot(ProviderKind.Codex, UsageStatus.Unknown, null, [], now, null),
            new ProviderSnapshot(
                ProviderKind.Grok,
                UsageStatus.Ok,
                null,
                [
                    new UsageWindow("Build", 10080, 10, 90, now.AddDays(1)),
                    new UsageWindow("Bot", 10080, 25, 75, now.AddDays(1))
                ],
                now,
                null),
            new ProviderSnapshot(ProviderKind.Agy, UsageStatus.Unknown, null, [], now, null),
            now,
            now);

        var options = TraySquareCatalog.Build(state)
            .Where(option => option.Provider == ProviderKind.Grok)
            .ToArray();

        var grok = Assert.Single(options);
        var build = Assert.Single(grok.Windows);
        Assert.Equal(TraySquareKeys.GrokWeekly, grok.Key);
        Assert.Equal("Build", build.Label);
    }

    [Fact]
    public void Tray_context_marshals_system_events_and_reapplies_launch_toggle()
    {
        var context = File.ReadAllText(FindSource("BuildnBits.Usage.Tray", "TrayApplicationContext.cs"));
        var preference = Block(context, "private void OnUserPreferenceChanged");
        var iconToggle = Block(context, "_icons.LaunchAtLoginToggled");
        var popupToggle = Block(context, "_popup.LaunchAtLoginChanged");

        Assert.Contains("PostToUi", preference, StringComparison.Ordinal);
        Assert.DoesNotContain("_icons.Apply", preference, StringComparison.Ordinal);
        Assert.Contains("ApplyIconsOnUi", iconToggle, StringComparison.Ordinal);
        Assert.Contains("ApplyIconsOnUi", popupToggle, StringComparison.Ordinal);
    }

    [Fact]
    public void Tray_menu_includes_floating_desktop_widget_toggle()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Exception? error = null;
        string[] labels = [];
        var floatingChecked = false;
        var thread = new Thread(() =>
        {
            try
            {
                using var host = new NotifyIconHost();
                var settings = new AppSettings { FloatingWidgetEnabled = true };
                host.Apply(CombinedUsageState.Empty, launchAtLogin: false, settings);
                var field = typeof(NotifyIconHost).GetField(
                    "_icons",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var icons = (System.Collections.IDictionary?)field?.GetValue(host);
                Assert.NotNull(icons);
                Assert.NotEmpty(icons!);
                var icon = icons.Values.Cast<NotifyIcon>().First();
                var menu = Assert.IsType<ContextMenuStrip>(icon.ContextMenuStrip);
                labels = menu.Items.OfType<ToolStripMenuItem>().Select(item => item.Text ?? "").ToArray();
                floatingChecked = menu.Items.OfType<ToolStripMenuItem>()
                    .Single(item => item.Text == "Show floating desktop widget")
                    .Checked;
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(error);
        Assert.Contains("Show floating desktop widget", labels);
        Assert.Contains("Launch at login", labels);
        Assert.True(floatingChecked);
    }

    [Fact]
    public void Tray_context_wires_floating_widget_menu_toggle()
    {
        var context = File.ReadAllText(FindSource("BuildnBits.Usage.Tray", "TrayApplicationContext.cs"));
        var host = File.ReadAllText(FindSource("BuildnBits.Usage.Tray", "Icons", "NotifyIconHost.cs"));
        var toggle = Block(context, "_icons.FloatingWidgetToggled");

        Assert.Contains("FloatingWidgetToggled", host, StringComparison.Ordinal);
        Assert.Contains("Show floating desktop widget", host, StringComparison.Ordinal);
        Assert.Contains("ApplyFloatingWidgetState", toggle, StringComparison.Ordinal);
        Assert.Contains("ApplyIconsOnUi", toggle, StringComparison.Ordinal);
    }

    [Fact]
    public void Grok_popup_renders_every_usage_window()
    {
        var popup = File.ReadAllText(FindSource("BuildnBits.Usage.Tray", "Ui", "UsagePopupForm.cs"));

        Assert.Contains("WindowsForPopup(ProviderKind.Grok, snapshot)", popup, StringComparison.Ordinal);
        Assert.Contains("return snapshot.Windows;", popup, StringComparison.Ordinal);
        Assert.DoesNotContain("snapshot.Weekly ?? snapshot.Windows.FirstOrDefault()", popup, StringComparison.Ordinal);
        Assert.Equal(3, popup.Split("AddOrderedRows(section", StringSplitOptions.None).Length - 1);
    }

    private static string Block(string source, string marker)
    {
        var start = source.IndexOf(marker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Marker not found: {marker}");
        var endMarker = marker.StartsWith("private ", StringComparison.Ordinal)
            ? "\n    }"
            : "\n        };";
        var end = source.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker not found: {marker}");
        return source[start..end];
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
