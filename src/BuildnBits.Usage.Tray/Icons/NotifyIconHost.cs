using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Tray.Icons;

/// <summary>
/// Owns three Shell_NotifyIcon-backed NotifyIcon instances. No Explorer injection or reparenting.
/// </summary>
public sealed class NotifyIconHost : IDisposable
{
    private readonly NotifyIcon _codex;
    private readonly NotifyIcon _grok;
    private readonly NotifyIcon _agy;
    private readonly TaskbarCreatedWindow _taskbarCreated;
    private Icon? _codexIcon;
    private Icon? _grokIcon;
    private Icon? _agyIcon;
    private CombinedUsageState _last = CombinedUsageState.Empty;
    private bool _launchAtLogin;
    private AppSettings _settings = new();

    public event EventHandler? PopupRequested;
    public event EventHandler? RefreshRequested;
    public event EventHandler? SettingsRequested;
    public event EventHandler? DiagnosticsRequested;
    public event EventHandler? ExitRequested;
    public event EventHandler<bool>? LaunchAtLoginToggled;
    public event EventHandler<int>? RefreshIntervalRequested;

    public NotifyIconHost()
    {
        _codex = CreateIcon("Codex");
        _grok = CreateIcon("Grok");
        _agy = CreateIcon("Google Antigravity [agy]");
        _taskbarCreated = new TaskbarCreatedWindow(RecreateAfterExplorerRestart);
    }

    public void Apply(CombinedUsageState state, bool launchAtLogin, AppSettings? settings = null)
    {
        _last = state;
        _launchAtLogin = launchAtLogin;
        _settings = settings ?? _settings;
        _settings.Normalize();
        TraySquareCatalog.EnsureAtLeastOneVisible(state, _settings);

        var codexOptions = TraySquareCatalog.SelectedOptions(state, ProviderKind.Codex, _settings);
        var grokOptions = TraySquareCatalog.SelectedOptions(state, ProviderKind.Grok, _settings);
        var agyOptions = TraySquareCatalog.SelectedOptions(state, ProviderKind.Agy, _settings);
        var codexWindows = TraySquareCatalog.SelectedWindows(state, ProviderKind.Codex, _settings);
        var grokWindows = TraySquareCatalog.SelectedWindows(state, ProviderKind.Grok, _settings);
        var agyWindows = TraySquareCatalog.SelectedWindows(state, ProviderKind.Agy, _settings);

        var highContrast = SystemInformation.HighContrast;
        var larger = _settings.LargerTrayDigits;
        var codexRemaining = LowestRemaining(codexWindows) is { } c
            ? PercentageMath.DisplayPercent(c)
            : (int?)null;
        var grokRemaining = LowestRemaining(grokWindows) is { } g
            ? PercentageMath.DisplayPercent(g)
            : (int?)null;
        var agyRemaining = LowestRemaining(agyWindows) is { } a
            ? PercentageMath.DisplayPercent(a)
            : (int?)null;

        Replace(ref _codexIcon, _codex, UsageIconRenderer.Create(ProviderKind.Codex, codexRemaining, highContrast, largerDigits: larger));
        Replace(ref _grokIcon, _grok, UsageIconRenderer.Create(ProviderKind.Grok, grokRemaining, highContrast, largerDigits: larger));
        Replace(ref _agyIcon, _agy, UsageIconRenderer.Create(ProviderKind.Agy, agyRemaining, highContrast, largerDigits: larger));

        _codex.Text = Truncate(CodexTooltip(state.Codex, codexWindows));
        _grok.Text = Truncate(GrokTooltip(state.Grok, grokWindows));
        _agy.Text = Truncate(AgyTooltip(state.Agy, agyWindows));
        _codex.Visible = codexOptions.Count > 0;
        _grok.Visible = grokOptions.Count > 0;
        _agy.Visible = agyOptions.Count > 0;

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
        ReplaceMenu(_codex, BuildMenu(launchAtLogin));
        ReplaceMenu(_grok, BuildMenu(launchAtLogin));
        ReplaceMenu(_agy, BuildMenu(launchAtLogin));
    }

    private static void ReplaceMenu(NotifyIcon icon, ContextMenuStrip next)
    {
        var previous = icon.ContextMenuStrip;
        icon.ContextMenuStrip = next;
        previous?.Dispose();
    }

    private ContextMenuStrip BuildMenu(bool launchAtLogin)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open usage", null, (_, _) => PopupRequested?.Invoke(this, EventArgs.Empty));
        menu.Items.Add("Refresh", null, (_, _) => RefreshRequested?.Invoke(this, EventArgs.Empty));
        var intervalMenu = new ToolStripMenuItem("Refresh interval");
        foreach (var minutes in AppSettings.AllowedRefreshIntervalMinutes)
        {
            var item = new ToolStripMenuItem($"{minutes} minutes")
            {
                Checked = _settings.RefreshIntervalMinutes == minutes,
                CheckOnClick = false
            };
            item.Click += (_, _) => RefreshIntervalRequested?.Invoke(this, minutes);
            intervalMenu.DropDownItems.Add(item);
        }

        menu.Items.Add(intervalMenu);
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
        _agy.Visible = false;
        Apply(_last, _launchAtLogin, _settings);
    }

    private static void Replace(ref Icon? field, NotifyIcon notify, Icon next)
    {
        notify.Icon = next;
        field?.Dispose();
        field = next;
    }

    private static double? LowestRemaining(IReadOnlyList<UsageWindow> windows) =>
        windows.Count == 0 ? null : windows.Min(window => window.RemainingPercent);

    private static string CodexTooltip(ProviderSnapshot snapshot, IReadOnlyList<UsageWindow> windows)
    {
        var plan = string.IsNullOrWhiteSpace(snapshot.PlanLabel) ? string.Empty : $" ({snapshot.PlanLabel})";
        var details = windows.Count == 0
            ? "unavailable"
            : string.Join(", ", windows.Select(window =>
                $"{window.Label} {window.RemainingPercent:0}% ({ResetCountdown.LocalResetLabel(window.ResetsAtUtc)})"));
        var lowest = LowestRemaining(windows);
        var lowestText = lowest is { } value ? $"{value:0}%" : "unavailable";
        return $"{RefreshAge(snapshot.FetchedAtUtc)} · Codex{plan} {lowestText} remaining. {details}. {snapshot.Status}";
    }

    private static string GrokTooltip(ProviderSnapshot snapshot, IReadOnlyList<UsageWindow> windows)
    {
        var plan = string.IsNullOrWhiteSpace(snapshot.PlanLabel) ? string.Empty : $" ({snapshot.PlanLabel})";
        var details = windows.Count == 0
            ? "unavailable"
            : string.Join(", ", windows.Select(window =>
                $"{window.Label} {window.RemainingPercent:0}% ({ResetCountdown.LocalResetLabel(window.ResetsAtUtc)})"));
        return $"{RefreshAge(snapshot.FetchedAtUtc)} · Grok{plan} {details}. {snapshot.Status}";
    }

    private static string AgyTooltip(ProviderSnapshot snapshot, IReadOnlyList<UsageWindow> windows)
    {
        var pools = windows.Count == 0
            ? "unavailable"
            : string.Join(", ", windows.Select(window =>
                $"{window.Label} {window.RemainingPercent:0}% ({ResetCountdown.LocalResetLabel(window.ResetsAtUtc)})"));
        var plan = string.IsNullOrWhiteSpace(snapshot.PlanLabel) ? string.Empty : $" ({snapshot.PlanLabel})";
        return $"{RefreshAge(snapshot.FetchedAtUtc)} · Google Antigravity [agy]{plan}: {pools}. {snapshot.Status}";
    }

    private static string RefreshAge(DateTimeOffset? updatedAtUtc)
    {
        if (updatedAtUtc is not { } updated)
        {
            return "Updated never";
        }

        var age = DateTimeOffset.UtcNow - updated;
        if (age < TimeSpan.FromMinutes(1))
        {
            return "Updated just now";
        }

        if (age < TimeSpan.FromHours(1))
        {
            return $"Updated {(int)age.TotalMinutes}m ago";
        }

        return $"Updated {(int)age.TotalHours}h ago";
    }

    private static string Truncate(string value) =>
        value.Length <= 127 ? value : value[..124] + "...";

    public void Dispose()
    {
        _codex.Visible = false;
        _grok.Visible = false;
        _agy.Visible = false;
        _codex.Dispose();
        _grok.Dispose();
        _agy.Dispose();
        _taskbarCreated.Dispose();
        _codexIcon?.Dispose();
        _grokIcon?.Dispose();
        _agyIcon?.Dispose();
    }
}
