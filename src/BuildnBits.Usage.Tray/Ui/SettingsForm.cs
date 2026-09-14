using System.Diagnostics;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Storage;
using BuildnBits.Usage.Tray.Icons;
using BuildnBits.Usage.Tray.Startup;

namespace BuildnBits.Usage.Tray.Ui;

public sealed class SettingsForm : Form
{
    private readonly AppSettingsStore _store;
    private readonly string _cachePath;
    private readonly CheckBox _launch = new() { Text = "Launch at login", AutoSize = true };
    private readonly CheckBox _larger = new() { Text = "Larger tray digits", AutoSize = true };
    private readonly ComboBox _interval = new()
    {
        DropDownStyle = ComboBoxStyle.DropDownList,
        Width = 180,
        IntegralHeight = true
    };
    private readonly Dictionary<string, CheckBox> _trayChecks =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Label _cache = new() { AutoSize = true };
    private readonly Label _widget = new()
    {
        AutoSize = true,
        Enabled = false,
        MaximumSize = new Size(460, 0),
        ForeColor = SystemColors.GrayText,
        Text =
            "Widgets Board card — Future Plan (disabled).\n" +
            "Windows only lists widgets from an installed MSIX package. This is not offered in the portable or unpackaged tray."
    };

    public AppSettings Result { get; private set; }

    public SettingsForm(
        AppSettings current,
        AppSettingsStore store,
        string cachePath,
        CombinedUsageState? state = null)
    {
        Result = current;
        _store = store;
        _cachePath = cachePath;
        var trayState = state ?? CombinedUsageState.Empty;
        var trayOptions = TraySquareCatalog.Build(trayState);

        Text = "BuildnBits Usage settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 460);
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(16);

        _launch.Checked = LaunchAtLogin.IsEnabled();
        _larger.Checked = current.LargerTrayDigits;
        _interval.Items.AddRange(["3 minutes", "5 minutes (default)", "10 minutes"]);
        _interval.SelectedIndex = current.RefreshIntervalMinutes switch
        {
            3 => 0,
            10 => 2,
            _ => 1
        };
        _cache.Text = $"Cache folder:\n{Path.GetDirectoryName(cachePath)}";

        var openCache = new Button { Text = "Open cache folder", AutoSize = true };
        var openWidgets = new Button { Text = "Open Widgets Board", AutoSize = true, Enabled = false };
        var copyPack = new Button { Text = "Copy pack command", AutoSize = true, Enabled = false };
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, AutoSize = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, AutoSize = true };
        AcceptButton = ok;
        CancelButton = cancel;

        openCache.Click += (_, _) =>
        {
            var dir = Path.GetDirectoryName(_cachePath);
            if (dir is not null)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", dir) { UseShellExecute = true });
            }
        };

        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = true
        };
        layout.Controls.Add(_launch);
        layout.Controls.Add(BuildTraySquareGroup(trayOptions, current));
        layout.Controls.Add(_larger);
        var intervalRow = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = false,
            Margin = new Padding(0, 8, 0, 0)
        };
        intervalRow.Controls.Add(new Label
        {
            Text = "Update every:",
            AutoSize = true,
            Padding = new Padding(0, 5, 8, 0)
        });
        intervalRow.Controls.Add(_interval);
        layout.Controls.Add(intervalRow);
        layout.Controls.Add(_widget);
        layout.Controls.Add(openWidgets);
        layout.Controls.Add(copyPack);
        layout.Controls.Add(_cache);
        layout.Controls.Add(openCache);
        var buttons = new FlowLayoutPanel { AutoSize = true };
        buttons.Controls.Add(ok);
        buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons);
        Controls.Add(layout);

        ok.Click += (_, _) =>
        {
            var currentVisibility = current.TraySquareVisibility ??
                new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            var next = new AppSettings
            {
                ShowCodexIcon = current.ShowCodexIcon,
                ShowGrokIcon = current.ShowGrokIcon,
                ShowAgyIcon = current.ShowAgyIcon,
                TraySquareVisibility = new Dictionary<string, bool>(
                    currentVisibility,
                    StringComparer.OrdinalIgnoreCase),
                LargerTrayDigits = _larger.Checked,
                RefreshIntervalMinutes = _interval.SelectedIndex switch
                {
                    0 => 3,
                    2 => 10,
                    _ => AppSettings.DefaultRefreshIntervalMinutes
                }
            };

            foreach (var option in trayOptions)
            {
                if (_trayChecks.TryGetValue(option.Key, out var check))
                {
                    next.SetTraySquareVisible(option.Key, check.Checked);
                }
            }

            next.Normalize();
            _store.Save(next);
            Result = next;
            LaunchAtLogin.SetEnabled(_launch.Checked);
        };
    }

    private GroupBox BuildTraySquareGroup(
        IReadOnlyList<TraySquareOption> options,
        AppSettings current)
    {
        var group = new GroupBox
        {
            Text = "Tray squares",
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Width = 470,
            Padding = new Padding(8, 20, 8, 8),
            Margin = new Padding(0, 8, 0, 0)
        };
        var checks = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Width = 450,
            Margin = new Padding(0)
        };

        foreach (var option in options)
        {
            var check = new CheckBox
            {
                Text = $"Show {option.DisplayLabel} square in tray",
                AutoSize = true,
                Checked = current.IsTraySquareVisible(option.Key, option.Provider),
                Margin = new Padding(0, 0, 0, 4)
            };
            _trayChecks.Add(option.Key, check);
            checks.Controls.Add(check);
        }

        group.Controls.Add(checks);
        return group;
    }

}
