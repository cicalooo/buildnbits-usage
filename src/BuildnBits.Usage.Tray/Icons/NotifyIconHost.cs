using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Tray.Icons;

/// <summary>
/// Owns two Shell_NotifyIcon-backed NotifyIcon instances. No Explorer injection or reparenting.
/// </summary>
public sealed class NotifyIconHost : IDisposable
{
    private readonly NotifyIcon _codex;
    private readonly NotifyIcon _grok;
    private readonly TaskbarCreatedWindow _taskbarCreated;
    private Icon? _codexIcon;
    private Icon? _grokIcon;
    private CombinedUsageState _last = CombinedUsageState.Empty;
    private bool _launchAtLogin;
    private AppSettings _settings = new();

    public event EventHandler? PopupRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? DiagnosticsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? LaunchAtLoginToggled;

    public NotifyIconHost()
    {
        _codex = CreateIcon("Codex");
        _grok = CreateIcon("Grok");
        _taskbarCreated = new TaskbarCreatedWindow(RecreateAfterExplorerRestart);
    }

    public void Apply(CombinedUsageState state, bool launchAtLogin, AppSettings? settings = null)
    {
        _last = state;
        _launchAtLogin = launchAtLogin;
        _settings = settings ?? _settings;
        if (!_settings.ShowCodexIcon && !_settings.ShowGrokIcon)
        {
            _settings.ShowCodexIcon = true;
        }

        var highContrast = SystemInformation.HighContrast;
        var larger = _settings.LargerTrayDigits;
        var codexRemaining = state.Codex.LowestRemainingPercent is { } c
            ? PercentageMath.DisplayPercent(c)
            : (int?)null;
        var grokRemaining = (state.Grok.Weekly?.RemainingPercent ?? state.Grok.LowestRemainingPercent) is { } g
            ? PercentageMath.DisplayPercent(g)
            : (int?)null;

        Replace(ref _codexIcon, _codex, UsageIconRenderer.Create(ProviderKind.Codex, codexRemaining, highContrast, largerDigits: larger));
        Replace(ref _grokIcon, _grok, UsageIconRenderer.Create(ProviderKind.Grok, grokRemaining, highContrast, largerDigits: larger));

        _codex.Text = Truncate(CodexTooltip(state.Codex));
        _grok.Text = Truncate(GrokTooltip(state.Grok));
        _codex.Visible = _settings.ShowCodexIcon;
        _grok.Visible = _settings.ShowGrokIcon;

        RebuildMenus(launchAtLogin);
    }

    private NotifyIcon CreateIcon(string name)
    {
        var icon = new NotifyIcon
        {
            Visible = true,
            Text = name
        };
        icon.MouseUp += (_, e) =>
        {
            if (e.Button is MouseButtons.Left)
            {
                PopupRequested?.Invoke(this, EventArgs.Empty);
            }
        };
        return icon;
    }

    private void RebuildMenus(bool launchAtLogin)
    {
        _codex.ContextMenuStrip = BuildMenu(launchAtLogin);
        _grok.ContextMenuStrip = BuildMenu(launchAtLogin);
    }

    private ContextMenuStrip BuildMenu(bool launchAtLogin)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open usage", null, (_, _) => PopupRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Refresh", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Settings", null, (_, _) => SettingsRequested?.Invoke(this, EventArgs.Empty));
        var login = new ToolStripMenuItem("Launch at login") { Checked = launchAtLogin, CheckOnClick = true };
        login.CheckedChanged += (_, _) => LaunchAtLoginToggled?.Invoke(this, login.Checked);
        menu.Items.Add(login);
        menu.Items.Add("Diagnostics", null, (_, _) => DiagnosticsRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty));
        return menu;
    }

    private void RecreateAfterExplorerRestart()
    {
        _codex.Visible = false;
        _grok.Visible = false;
        Apply(_last, _launchAtLogin, _settings);
    }

    private static void Replace(ref Icon? field, NotifyIcon notify, Icon next)
    {
        notify.Icon = next;
        field?.Dispose();
        field = next;
    }

    private static string CodexTooltip(ProviderSnapshot snapshot)
    {
        var five = snapshot.WindowByDuration(300);
        var week = snapshot.WindowByDuration(10080);
        var lowest = snapshot.LowestRemainingPercent;
        return $"Codex {lowest:0}% remaining. 5h {five?.RemainingPercent:0}% ({ResetCountdown.LocalResetLabel(five?.ResetsAtUtc)}). 7d {week?.RemainingPercent:0}% ({ResetCountdown.LocalResetLabel(week?.ResetsAtUtc)}). {snapshot.Status}";
    }

    private static string GrokTooltip(ProviderSnapshot snapshot)
    {
        var week = snapshot.Weekly ?? snapshot.Windows.FirstOrDefault();
        return $"Grok weekly {week?.RemainingPercent:0}% remaining ({ResetCountdown.LocalResetLabel(week?.ResetsAtUtc)}). {snapshot.Status}";
    }

    private static string Truncate(string value) =>
        value.Length <= 127 ? value : value[..124] + "...";

    public void Dispose()
    {
        _codex.Visible = false;
        _grok.Visible = false;
        _codex.Dispose();
        _grok.Dispose();
        _taskbarCreated.Dispose();
        _codexIcon?.Dispose();
        _grokIcon?.Dispose();
    }
}
