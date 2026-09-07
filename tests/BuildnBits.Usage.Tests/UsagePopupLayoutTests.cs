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
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new UsagePopupForm();
                var reset = DateTimeOffset.UtcNow.AddHours(1);
                var state = new CombinedUsageState(
                    new ProviderSnapshot(
                        ProviderKind.Codex,
                        UsageStatus.Ok,
                        "Plus",
                        [
                            new UsageWindow("5-hour", 300, 20, 80, reset),
                            new UsageWindow("7-day", 10080, 40, 60, reset)
                        ],
                        DateTimeOffset.UtcNow,
                        null),
                    new ProviderSnapshot(
                        ProviderKind.Grok,
                        UsageStatus.Ok,
                        "SuperGrok",
                        [new UsageWindow("Weekly", 10080, 10, 90, reset)],
                        DateTimeOffset.UtcNow,
                        null),
                    new ProviderSnapshot(
                        ProviderKind.Agy,
                        UsageStatus.Ok,
                        "Standard",
                        [
                            new UsageWindow("Claude", null, 25, 75, reset),
                            new UsageWindow("Gemini", null, 50, 50, reset)
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
    }
}
