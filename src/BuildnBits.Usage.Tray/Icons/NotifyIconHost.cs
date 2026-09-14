using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Tray.Icons;

/// <summary>
/// Owns one Shell_NotifyIcon-backed NotifyIcon instance for each selected tray option.
/// </summary>
public sealed class NotifyIconHost : IDisposable
{
    private readonly Dictionary<string, NotifyIcon> _icons =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Icon> _iconImages =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly TaskbarCreatedWindow _taskbarCreated;
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
        _taskbarCreated = new TaskbarCreatedWindow(RecreateAfterExplorerRestart);
    }

    public void Apply(CombinedUsageState state, bool launchAtLogin, AppSettings? settings = null)
    {
        _last = state;
        _launchAtLogin = launchAtLogin;
        _settings = settings ?? _settings;
        _settings.Normalize();
        TraySquareCatalog.EnsureAtLeastOneVisible(state, _settings);

        var selectedOptions = TraySquareCatalog.Build(state)
            .Where(option => _settings.IsTraySquareVisible(option.Key, option.Provider))
            .ToArray();
        var selectedKeys = selectedOptions
            .Select(option => option.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var key in _icons.Keys.Where(key => !selectedKeys.Contains(key)).ToArray())
        {
            RemoveIcon(key);
        }

        var highContrast = SystemInformation.HighContrast;
        var larger = _settings.LargerTrayDigits;
        foreach (var option in selectedOptions)
        {
            var icon = GetOrCreateIcon(option);
            var snapshot = SnapshotFor(state, option.Provider);
            var remaining = LowestRemaining(option.Windows) is { } value
                ? PercentageMath.DisplayPercent(value)
                : (int?)null;
            var nextImage = UsageIconRenderer.Create(
                option.Provider,
                remaining,
                highContrast,
                largerDigits: larger);
            icon.Icon = nextImage;
            if (_iconImages.Remove(option.Key, out var previousImage))
            {
                previousImage.Dispose();
            }

            _iconImages[option.Key] = nextImage;
            icon.Text = Truncate(OptionTooltip(option, snapshot, option.Windows));
            icon.Visible = true;
        }

        RebuildMenus(launchAtLogin);
    }

    private NotifyIcon GetOrCreateIcon(TraySquareOption option)
    {
        if (_icons.TryGetValue(option.Key, out var existing))
        {
            return existing;
        }

        var created = CreateIcon(option.DisplayLabel);
        _icons.Add(option.Key, created);
        return created;
    }

    private NotifyIcon CreateIcon(string name)
    {
        var icon = new NotifyIcon
        {
            Visible = false,
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
        foreach (var icon in _icons.Values)
        {
            ReplaceMenu(icon, BuildMenu(launchAtLogin));
        }
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
        foreach (var icon in _icons.Values)
        {
            icon.Visible = false;
        }

        Apply(_last, _launchAtLogin, _settings);
    }

    private void RemoveIcon(string key)
    {
        if (_icons.Remove(key, out var icon))
        {
            icon.Visible = false;
            var menu = icon.ContextMenuStrip;
            icon.ContextMenuStrip = null;
            menu?.Dispose();
            icon.Dispose();
        }

        if (_iconImages.Remove(key, out var image))
        {
            image.Dispose();
        }
    }

    private static ProviderSnapshot SnapshotFor(CombinedUsageState state, ProviderKind provider) => provider switch
    {
        ProviderKind.Codex => state.Codex,
        ProviderKind.Grok => state.Grok,
        ProviderKind.Agy => state.Agy,
        _ => state.Agy
    };

    private static double? LowestRemaining(IReadOnlyList<UsageWindow> windows) =>
        windows.Count == 0 ? null : windows.Min(window => window.RemainingPercent);

    private static string OptionTooltip(
        TraySquareOption option,
        ProviderSnapshot snapshot,
        IReadOnlyList<UsageWindow> windows)
    {
        var plan = string.IsNullOrWhiteSpace(snapshot.PlanLabel) ? string.Empty : $" ({snapshot.PlanLabel})";
        var details = windows.Count == 0
            ? "unavailable"
            : string.Join(", ", windows.Select(window =>
                $"{window.Label} {window.RemainingPercent:0}% ({ResetCountdown.LocalResetLabel(window.ResetsAtUtc)})"));
        var lowest = LowestRemaining(windows);
        var lowestText = lowest is { } value ? $"{value:0}%" : "unavailable";
        return $"{RefreshAge(snapshot.FetchedAtUtc)} · {option.DisplayLabel}{plan} {lowestText} remaining. {details}. {snapshot.Status}";
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
        foreach (var key in _icons.Keys.ToArray())
        {
            RemoveIcon(key);
        }

        _taskbarCreated.Dispose();
    }
}
