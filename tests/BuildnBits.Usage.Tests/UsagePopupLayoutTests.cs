using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Storage;
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
        var codexLabels = string.Empty;
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
                            new UsageWindow("5-hour duplicate", 300, 30, 70, resetSoon),
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
                            new UsageWindow("Bot", 10080, 25, 75, resetLater),
                            new UsageWindow("Future", 123, 30, 70, resetSoon)
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

                // Codex is providers.Controls[0]; Grok is [1]; Agy is [2]
                var codexSection = providers.Controls[0];
                var grokSection = providers.Controls[1];
                var agySection = providers.Controls[2];
                codexLabels = string.Join("|", FindRowLabels(codexSection));
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
        Assert.Contains("5h duplicate", codexLabels);
        Assert.Contains("Build", grokLabels);
        Assert.Contains("Bot", grokLabels);
        Assert.Contains("Future", grokLabels);
        Assert.Equal("Gemini 5h", firstAgyLabel);
    }

    [Fact]
    public void Pinning_switches_to_desktop_widget_chrome()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Exception? error = null;
        var stayedVisible = false;
        var showedInTaskbar = false;
        var borderIsToolWindow = false;
        var isPinned = false;
        var pinLabelPresent = false;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new UsagePopupForm();
                form.Bind(CombinedUsageState.Empty, launchAtLogin: false);
                form.CreateControl();
                pinLabelPresent = Descendants(form)
                    .OfType<CheckBox>()
                    .Any(check => check.Text == "Pin as desktop widget");
                form.SetPinned(true, raiseEvent: false);
                form.Show();
                stayedVisible = form.Visible;
                showedInTaskbar = form.ShowInTaskbar;
                borderIsToolWindow = form.FormBorderStyle == FormBorderStyle.SizableToolWindow;
                isPinned = form.IsPinned;
                form.Close();
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
        Assert.True(pinLabelPresent);
        Assert.True(stayedVisible);
        Assert.True(showedInTaskbar);
        Assert.True(borderIsToolWindow);
        Assert.True(isPinned);
    }

    [Fact]
    public void Usage_row_percent_font_fits_above_progress_bar()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        Exception? error = null;
        var rowHeight = 0;
        var percentHeight = 0;
        var contentHeight = 0;
        var percentFontSize = 0f;
        var thread = new Thread(() =>
        {
            try
            {
                using var form = new UsagePopupForm();
                var now = DateTimeOffset.UtcNow;
                var state = new CombinedUsageState(
                    new ProviderSnapshot(ProviderKind.Codex, UsageStatus.Unknown, null, [], now, null),
                    new ProviderSnapshot(
                        ProviderKind.Grok,
                        UsageStatus.Ok,
                        "SuperGrok",
                        [new UsageWindow("Build", 10080, 10, 90, now.AddDays(1))],
                        now,
                        null),
                    new ProviderSnapshot(ProviderKind.Agy, UsageStatus.Unknown, null, [], now, null),
                    now,
                    now);
                var settings = new AppSettings
                {
                    ShowCodexIcon = false,
                    ShowGrokIcon = true,
                    ShowAgyIcon = false,
                    TraySquareVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                    {
                        [TraySquareKeys.CodexFiveHour] = false,
                        [TraySquareKeys.CodexSevenDay] = false,
                        [TraySquareKeys.GrokWeekly] = true,
                        [TraySquareKeys.AgyWeekly] = false
                    }
                };
                form.Bind(state, launchAtLogin: false, settings);
                form.CreateControl();
                form.PerformLayout();

                var layout = (TableLayoutPanel)form.Controls[0];
                var providers = (FlowLayoutPanel)layout.Controls[0];
                var row = providers.Controls
                    .Cast<Control>()
                    .SelectMany(section => section.Controls.OfType<FlowLayoutPanel>())
                    .SelectMany(panel => panel.Controls.Cast<Control>())
                    .First(control => control.GetType().Name == "UsageRow");
                var percent = row.Controls
                    .OfType<TableLayoutPanel>()
                    .SelectMany(panel => panel.Controls.OfType<Label>())
                    .First(label => label.Text.Contains('%', StringComparison.Ordinal));
                rowHeight = row.Height;
                contentHeight = row.ClientSize.Height;
                percentHeight = TextRenderer.MeasureText(
                    percent.Text,
                    percent.Font,
                    new Size(short.MaxValue, short.MaxValue),
                    TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Height;
                percentFontSize = percent.Font.SizeInPoints;
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
        Assert.True(rowHeight >= 34, $"Row height {rowHeight} is too short.");
        Assert.True(contentHeight > percentHeight, $"Percent glyph {percentHeight}px does not fit in content {contentHeight}px.");
        Assert.True(percentFontSize >= 13f, $"Percent font {percentFontSize}pt is too small.");
    }

    [Fact]
    public void Unpinned_popup_keeps_all_provider_sections_regardless_of_tray_visibility()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sectionTitles = string.Empty;
        var statusText = string.Empty;
        RunSta(() =>
        {
            using var form = new UsagePopupForm();
            form.Bind(
                CreateVisibilityState(),
                launchAtLogin: false,
                CreateGrokOnlyTraySettings(),
                pinned: false);
            form.CreateControl();
            form.PerformLayout();
            (sectionTitles, statusText) = ReadSectionsAndStatus(form);
        });

        Assert.Contains("Codex", sectionTitles, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Grok", sectionTitles, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Antigravity", sectionTitles, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex", statusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Grok", statusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Antigravity", statusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pinned_widget_hides_providers_unchecked_in_tray_settings()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var sectionTitles = string.Empty;
        var statusText = string.Empty;
        RunSta(() =>
        {
            using var form = new UsagePopupForm();
            form.Bind(
                CreateVisibilityState(),
                launchAtLogin: false,
                CreateGrokOnlyTraySettings(),
                pinned: true);
            form.CreateControl();
            form.PerformLayout();
            (sectionTitles, statusText) = ReadSectionsAndStatus(form);
        });

        Assert.Contains("Grok", sectionTitles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Codex", sectionTitles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Antigravity", sectionTitles, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Codex", statusText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Grok", statusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pinned_widget_keeps_all_rows_when_any_provider_square_is_visible()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var codexLabels = string.Empty;
        RunSta(() =>
        {
            using var form = new UsagePopupForm();
            var settings = new AppSettings
            {
                TraySquareVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
                {
                    [TraySquareKeys.CodexFiveHour] = true,
                    [TraySquareKeys.CodexSevenDay] = false,
                    [TraySquareKeys.GrokWeekly] = false,
                    [TraySquareKeys.AgyWeekly] = false
                }
            };
            form.Bind(CreateVisibilityState(), launchAtLogin: false, settings, pinned: true);
            form.CreateControl();
            form.PerformLayout();

            var layout = Assert.IsType<TableLayoutPanel>(form.Controls[0]);
            var providers = Assert.IsType<FlowLayoutPanel>(layout.Controls[0]);
            var codex = Assert.Single(providers.Controls.Cast<Control>());
            codexLabels = string.Join("|", FindRowLabels(codex));
        });

        Assert.Contains("5h", codexLabels, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("7d", codexLabels, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Pin_checkbox_switches_between_complete_popup_and_filtered_widget()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        var before = string.Empty;
        var pinned = string.Empty;
        var unpinned = string.Empty;
        RunSta(() =>
        {
            using var form = new UsagePopupForm();
            form.Bind(
                CreateVisibilityState(),
                launchAtLogin: false,
                CreateGrokOnlyTraySettings(),
                pinned: false);
            form.CreateControl();
            form.PerformLayout();
            before = ReadSectionsAndStatus(form).Sections;

            var pin = Descendants(form)
                .OfType<CheckBox>()
                .Single(check => check.Text == "Pin as desktop widget");
            pin.Checked = true;
            form.PerformLayout();
            pinned = ReadSectionsAndStatus(form).Sections;

            pin.Checked = false;
            form.PerformLayout();
            unpinned = ReadSectionsAndStatus(form).Sections;
        });

        Assert.Contains("Codex", before, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Codex", pinned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Grok", pinned, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Antigravity", pinned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Codex", unpinned, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Antigravity", unpinned, StringComparison.OrdinalIgnoreCase);
    }

    private static CombinedUsageState CreateVisibilityState()
    {
        var now = DateTimeOffset.UtcNow;
        return new CombinedUsageState(
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
                [new UsageWindow("Gemini Models · Weekly Limit Remaining", 10080, 50, 50, now.AddDays(4))],
                now,
                null),
            now,
            now);
    }

    private static AppSettings CreateGrokOnlyTraySettings() => new()
    {
        TraySquareVisibility = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase)
        {
            [TraySquareKeys.CodexFiveHour] = false,
            [TraySquareKeys.CodexSevenDay] = false,
            [TraySquareKeys.GrokWeekly] = true,
            [TraySquareKeys.AgyWeekly] = false
        }
    };

    private static (string Sections, string Status) ReadSectionsAndStatus(UsagePopupForm form)
    {
        var layout = Assert.IsType<TableLayoutPanel>(form.Controls[0]);
        var providers = Assert.IsType<FlowLayoutPanel>(layout.Controls[0]);
        var sections = string.Join(
            "|",
            providers.Controls.Cast<Control>().Select(control =>
                control.Controls.OfType<Label>().FirstOrDefault()?.Text ?? control.Text));
        var status = layout.Controls.OfType<Label>().FirstOrDefault()?.Text ?? string.Empty;
        return (sections, status);
    }

    private static void RunSta(Action action)
    {
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
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
