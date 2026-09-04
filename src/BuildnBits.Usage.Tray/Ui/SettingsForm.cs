using System.Diagnostics;
using BuildnBits.Usage.Core.Storage;
using BuildnBits.Usage.Tray.Startup;

namespace BuildnBits.Usage.Tray.Ui;

public sealed class SettingsForm : Form
{
    private readonly AppSettingsStore _store;
    private readonly string _cachePath;
    private readonly CheckBox _launch = new() { Text = "Launch at login", AutoSize = true };
    private readonly CheckBox _codex = new() { Text = "Show Codex notification-area icon", AutoSize = true };
    private readonly CheckBox _grok = new() { Text = "Show Grok notification-area icon", AutoSize = true };
    private readonly CheckBox _agy = new() { Text = "Show Google Antigravity [agy] notification-area icon", AutoSize = true };
    private readonly CheckBox _larger = new() { Text = "Larger tray digits", AutoSize = true };
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

    public SettingsForm(AppSettings current, AppSettingsStore store, string cachePath)
    {
        Result = current;
        _store = store;
        _cachePath = cachePath;
        Text = "BuildnBits Usage settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 360);
        MaximizeBox = false;
        MinimizeBox = false;
        Font = new Font("Segoe UI", 9.5f);
        Padding = new Padding(16);

        _launch.Checked = LaunchAtLogin.IsEnabled();
        _codex.Checked = current.ShowCodexIcon;
        _grok.Checked = current.ShowGrokIcon;
        _agy.Checked = current.ShowAgyIcon;
        _larger.Checked = current.LargerTrayDigits;
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
        layout.Controls.Add(_codex);
        layout.Controls.Add(_grok);
        layout.Controls.Add(_agy);
        layout.Controls.Add(_larger);
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
            if (!_codex.Checked && !_grok.Checked && !_agy.Checked)
            {
                _codex.Checked = true;
            }

            Result = new AppSettings
            {
                ShowCodexIcon = _codex.Checked,
                ShowGrokIcon = _grok.Checked,
                ShowAgyIcon = _agy.Checked,
                LargerTrayDigits = _larger.Checked
            };
            _store.Save(Result);
            LaunchAtLogin.SetEnabled(_launch.Checked);
        };
    }
}
