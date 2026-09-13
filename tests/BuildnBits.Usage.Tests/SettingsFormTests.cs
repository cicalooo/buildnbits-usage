using System.Windows.Forms;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Storage;
using BuildnBits.Usage.Tray.Ui;

namespace BuildnBits.Usage.Tests;

public sealed class SettingsFormTests
{
    [Fact]
    public void Settings_lists_one_checkbox_for_each_known_tray_period()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var dir = Path.Combine(Path.GetTempPath(), "bnb-settings-ui-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var now = DateTimeOffset.UtcNow;
        var state = new CombinedUsageState(
            new ProviderSnapshot(
                ProviderKind.Codex,
                UsageStatus.Ok,
                null,
                [
                    new UsageWindow("5-hour", 300, 20, 80, now.AddHours(1)),
                    new UsageWindow("7-day", 10080, 40, 60, now.AddDays(2))
                ],
                now,
                null),
            new ProviderSnapshot(
                ProviderKind.Grok,
                UsageStatus.Ok,
                null,
                [new UsageWindow("Weekly", 10080, 10, 90, now.AddDays(3))],
                now,
                null),
            new ProviderSnapshot(
                ProviderKind.Agy,
                UsageStatus.Ok,
                null,
                [new UsageWindow("Gemini", 10080, 25, 75, now.AddDays(4))],
                now,
                null),
            now,
            now);

        using var form = new SettingsForm(
            new AppSettings(),
            new AppSettingsStore(dir),
            Path.Combine(dir, "usage-cache.json"),
            state);

        var labels = Descendants(form)
            .OfType<CheckBox>()
            .Select(check => check.Text)
            .Where(text => text.StartsWith("Show ", StringComparison.Ordinal))
            .ToArray();

        Assert.Contains("Show Codex 5-hour square in tray", labels);
        Assert.Contains("Show Codex 7-day square in tray", labels);
        Assert.Contains("Show Grok weekly square in tray", labels);
        Assert.Contains("Show Antigravity weekly square in tray", labels);
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (Control child in root.Controls)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
            {
                yield return descendant;
            }
        }
    }
}
