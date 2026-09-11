using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Tray.Ui;

namespace BuildnBits.Usage.Tests;

public sealed class UsagePopupLayoutTests
{
    [Fact]
    public void Bound_provider_sections_have_visible_layout()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Exception? error = null;
        var providerHeight = 0;
        var sectionCount = 0;
        var allSectionsVisible = false;
        var sectionDetails = string.Empty;
        var firstAgyLabel = string.Empty;
        var grokLabels = string.Empty;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new UsagePopupForm();
                var resetSoon = DateTimeOffset.UtcNow.AddHours(1);
                var resetLater = DateTimeOffset.UtcNow.AddDays(2);
                var state = new CombinedUsageState(
                    new ProviderSnapshot(
                        ProviderKind.Codex,
                        UsageStatus.Ok,
                        "Plus",
                        [
                            new UsageWindow("5-hour", 300, 20, 80, resetSoon),
                            new UsageWindow("7-day", 10080, 40, 60, resetLater)
                        ],
                        DateTimeOffset.UtcNow,
                        null),
                    new ProviderSnapshot(
                        ProviderKind.Grok,
                        UsageStatus.Ok,
                        "SuperGrok",
                        [
                            new UsageWindow("Build", 10080, 10, 90, resetLater),
                            new UsageWindow("Bot", 10080, 25, 75, resetLater)
                        ],
                        DateTimeOffset.UtcNow,
                        null),
                    new ProviderSnapshot(
                        ProviderKind.Agy,
                        UsageStatus.Ok,
                        "Standard",
                        [
                            new UsageWindow("Gemini Models · Weekly Limit Remaining", null, 50, 50, resetLater),
                            new UsageWindow("Gemini Models · Five Hour Limit Remaining", null, 25, 75, resetSoon)
                        ],
                        DateTimeOffset.UtcNow,
                        null),
                    DateTimeOffset.UtcNow,
                    DateTimeOffset.UtcNow);

                form.Bind(state, launchAtLogin: false);
                form.CreateControl();
                form.PerformLayout();

                var layout = (TableLayoutPanel)form.Controls[0];
                var providers = (FlowLayoutPanel)layout.Controls[0];
                providers.PerformLayout();
                providerHeight = providers.Height;
                sectionCount = providers.Controls.Count;
                sectionDetails = string.Join(
                    "; ",
                    providers.Controls.Cast<Control>().Select(control =>
                        $"visible={control.Visible},height={control.Height},width={control.Width},preferred={control.PreferredSize}"));
                allSectionsVisible = providers.Controls
                    .Cast<Control>()
                    .All(control => !control.IsDisposed && control.Height > 0);

                // Grok is providers.Controls[1]; Agy is [2]
                var grokSection = providers.Controls[1];
                var agySection = providers.Controls[2];
                grokLabels = string.Join("|", FindRowLabels(grokSection));
                firstAgyLabel = FindRowLabels(agySection).FirstOrDefault() ?? "";
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
        Assert.Equal(3, sectionCount);
        Assert.True(providerHeight > 0);
        Assert.True(allSectionsVisible, sectionDetails);
        Assert.Contains("Build", grokLabels);
        Assert.Contains("Bot", grokLabels);
        Assert.Equal("Gemini 5h", firstAgyLabel);
    }

    private static IEnumerable<string> FindRowLabels(Control section)
    {
        foreach (Control child in section.Controls)
        {
            foreach (var label in EnumerateLabels(child))
            {
                yield return label;
            }
        }
    }

    private static IEnumerable<string> EnumerateLabels(Control root)
    {
        if (root is Label label &&
            root.Parent is TableLayoutPanel table &&
            table.GetColumn(label) == 0 &&
            table.Parent is not null &&
            table.Parent.GetType().Name == "UsageRow")
        {
            yield return label.Text;
        }

        foreach (Control child in root.Controls)
        {
            foreach (var nested in EnumerateLabels(child))
            {
                yield return nested;
            }
        }
    }
}
