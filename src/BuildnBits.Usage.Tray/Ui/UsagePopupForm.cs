using System.Drawing.Drawing2D;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;
using BuildnBits.Usage.Core.Providers.Codex;
using BuildnBits.Usage.Core.Refresh;
using BuildnBits.Usage.Core.Storage;
using BuildnBits.Usage.Tray.Icons;

namespace BuildnBits.Usage.Tray.Ui;

public sealed class UsagePopupForm : Form
{
    private const int PopupWidth = 430;
    private const int DefaultMaxHeight = 560;
    private readonly FlowLayoutPanel _providers = new()
    {
        AutoScroll = true,
        FlowDirection = FlowDirection.TopDown,
        WrapContents = false,
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };
    private readonly TableLayoutPanel _layout = new()
    {
        ColumnCount = 1,
        RowCount = 3,
        Dock = DockStyle.Fill,
        Margin = new Padding(0),
        Padding = new Padding(0)
    };
    private readonly Label _status = new()
    {
        AutoSize = true,
        Margin = new Padding(0, 4, 0, 6),
        MaximumSize = new Size(PopupWidth - 24, 0)
    };
    private readonly Button _refresh = StyledButton("Refresh");
    private readonly Button _settings = StyledButton("Settings");
    private readonly Button _diagnostics = StyledButton("Diagnostics");
    private readonly CheckBox _launch = new()
    {
        Text = "Launch at login",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(0, 4, 8, 6)
    };
    private readonly CheckBox _pin = new()
    {
        Text = "Pin as desktop widget",
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Margin = new Padding(0, 4, 8, 6)
    };
    private readonly Button _exit = StyledButton("Exit");
    private readonly List<ProviderSection> _sections = [];
    private int _maxPopupHeight = DefaultMaxHeight;
    private CombinedUsageState _boundState = CombinedUsageState.Empty;
    private AppSettings _boundSettings = new();
    private RefreshProgress _progress = RefreshProgress.Idle;
    private bool _suppressPinEvent;
    private bool _restoringLocation;

    public event EventHandler? RefreshClicked;
    public event EventHandler? SettingsClicked;
    public event EventHandler? DiagnosticsClicked;
    public event EventHandler? ExitClicked;
    public event EventHandler<bool>? LaunchAtLoginChanged;
    public event EventHandler<bool>? PinWidgetChanged;
    public event EventHandler? PinnedLocationChanged;
    public bool IsPinned => _pin.Checked;

    public UsagePopupForm()
    {
        Text = "BuildnBits Usage";
        FormBorderStyle = FormBorderStyle.None;
        StartPosition = FormStartPosition.Manual;
        ShowInTaskbar = false;
        TopMost = true;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = true;
        BackColor = Color.FromArgb(32, 32, 36);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);
        Padding = new Padding(12);
        ClientSize = new Size(PopupWidth, 240);
        MinimumSize = new Size(PopupWidth, 180);
        MaximumSize = new Size(PopupWidth, DefaultMaxHeight);

        _layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _layout.Controls.Add(_providers, 0, 0);
        _layout.Controls.Add(_status, 0, 1);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Dock = DockStyle.Fill,
            Margin = new Padding(0)
        };
        actions.Controls.Add(_refresh);
        actions.Controls.Add(_settings);
        actions.Controls.Add(_diagnostics);
        actions.Controls.Add(_launch);
        actions.Controls.Add(_pin);
        actions.Controls.Add(_exit);
        _layout.Controls.Add(actions, 0, 2);
        Controls.Add(_layout);

        _refresh.Click += (_, _) => RefreshClicked?.Invoke(this, EventArgs.Empty);
        _settings.Click += (_, _) => SettingsClicked?.Invoke(this, EventArgs.Empty);
        _diagnostics.Click += (_, _) => DiagnosticsClicked?.Invoke(this, EventArgs.Empty);
        _exit.Click += (_, _) => ExitClicked?.Invoke(this, EventArgs.Empty);
        _launch.CheckedChanged += (_, _) => LaunchAtLoginChanged?.Invoke(this, _launch.Checked);
        _pin.CheckedChanged += (_, _) =>
        {
            ApplyPinnedMode();
            if (!_suppressPinEvent)
            {
                PinWidgetChanged?.Invoke(this, _pin.Checked);
            }
        };
        Deactivate += (_, _) =>
        {
            if (!IsPinned)
            {
                Hide();
            }
        };
        ResizeEnd += (_, _) =>
        {
            if (!_restoringLocation && IsPinned && Visible)
            {
                PinnedLocationChanged?.Invoke(this, EventArgs.Empty);
            }
        };
        FormClosing += (_, e) =>
        {
            if (e.CloseReason != CloseReason.UserClosing)
            {
                return;
            }

            // Keep the singleton popup alive for the tray host; treat Close as unpin/hide.
            e.Cancel = true;
            if (IsPinned)
            {
                SetPinned(false);
            }

            Hide();
        };
        Paint += (_, e) =>
        {
            if (FormBorderStyle == FormBorderStyle.None)
            {
                using var pen = new Pen(Color.FromArgb(70, 70, 76));
                e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
            }
        };
    }

    public void Bind(
        CombinedUsageState state,
        bool launchAtLogin,
        AppSettings? settings = null,
        bool? pinned = null)
    {
        _boundState = state;
        _boundSettings = settings ?? _boundSettings;
        _boundSettings.Normalize();
        ClearSections();
        if (IsProviderVisible(ProviderKind.Codex))
        {
            _sections.Add(BuildCodexSection(state.Codex));
        }

        if (IsProviderVisible(ProviderKind.Grok))
        {
            _sections.Add(BuildGrokSection(state.Grok));
        }

        if (IsProviderVisible(ProviderKind.Agy))
        {
            _sections.Add(BuildAgySection(state.Agy));
        }

        foreach (var section in _sections)
        {
            _providers.Controls.Add(section);
        }

        var visibleSnapshots = VisibleSnapshots(state).ToArray();
        var issueSnapshot = visibleSnapshots.FirstOrDefault(snapshot => snapshot.Status is not UsageStatus.Ok);
        var stale = visibleSnapshots.Any(snapshot => snapshot.Status is UsageStatus.Stale or UsageStatus.Error);
        var detail = issueSnapshot?.StatusMessage is { } message
            ? AppLog.Sanitize(message)
            : issueSnapshot?.Status is UsageStatus.Unknown
                ? "Waiting for the first refresh."
                : stale ? "Showing last successful values." : "Up to date.";
        UpdateStatus(detail);
        _launch.Checked = launchAtLogin;
        if (pinned is not null)
        {
            SetPinned(pinned.Value, raiseEvent: false);
        }

        ApplyTheme();
        ResizeForContent();
    }

    public void SetRefreshProgress(RefreshProgress progress)
    {
        _progress = progress;
        _refresh.Enabled = !progress.IsRefreshing;
        UpdateStatus();
        // Pinned widgets keep a stable size during refresh status churn.
        if (!IsPinned)
        {
            ResizeForContent();
        }
    }

    private void UpdateStatus(string? detail = null)
    {
        var state = _boundState;
        var visibleSnapshots = VisibleSnapshots(state).ToArray();
        if (detail is null)
        {
            var issueSnapshot = visibleSnapshots.FirstOrDefault(snapshot => snapshot.Status is not UsageStatus.Ok);
            var stale = visibleSnapshots.Any(snapshot => snapshot.Status is UsageStatus.Stale or UsageStatus.Error);
            detail = issueSnapshot?.StatusMessage is { } message
                ? AppLog.Sanitize(message)
                : issueSnapshot?.Status is UsageStatus.Unknown
                    ? "Waiting for the first refresh."
                    : stale ? "Showing last successful values." : "Up to date.";
        }

        var providerLine = visibleSnapshots.Length == 0
            ? "No providers selected in Settings."
            : string.Join(
                " · ",
                visibleSnapshots.Select(snapshot =>
                    $"{ProviderLabel(snapshot.Provider)} {snapshot.Status} ({RefreshAge(snapshot.FetchedAtUtc)})"));
        if (_progress.IsRefreshing)
        {
            var pending = _progress.PendingProviders
                .Where(IsProviderVisible)
                .Select(ProviderLabel)
                .ToArray();
            detail = pending.Length == 0
                ? "Refreshing — finishing."
                : $"Refreshing — waiting for {string.Join(", ", pending)}.";
        }

        _status.Text = $"{providerLine}{Environment.NewLine}{detail}";
    }

    private bool IsProviderVisible(ProviderKind provider) =>
        TraySquareCatalog.Build(_boundState)
            .Where(option => option.Provider == provider)
            .Any(option => _boundSettings.IsTraySquareVisible(option.Key, option.Provider));

    private IEnumerable<ProviderSnapshot> VisibleSnapshots(CombinedUsageState state)
    {
        if (IsProviderVisible(ProviderKind.Codex))
        {
            yield return state.Codex;
        }

        if (IsProviderVisible(ProviderKind.Grok))
        {
            yield return state.Grok;
        }

        if (IsProviderVisible(ProviderKind.Agy))
        {
            yield return state.Agy;
        }
    }

    private IReadOnlyList<UsageWindow> WindowsForPopup(ProviderKind provider, ProviderSnapshot snapshot)
    {
        // Grok's only tray square is Build; when it is enabled, show the full Grok snapshot
        // (Build, Bot, etc.) in the popup.
        if (provider == ProviderKind.Grok)
        {
            return snapshot.Windows;
        }

        return TraySquareCatalog.SelectedWindows(_boundState, provider, _boundSettings);
    }

    private static string ProviderLabel(ProviderKind provider) => provider switch
    {
        ProviderKind.Codex => "Codex",
        ProviderKind.Grok => "Grok",
        _ => "Antigravity"
    };

    public void ShowNearCursor()
    {
        if (IsPinned && Visible)
        {
            Activate();
            return;
        }

        var pos = Cursor.Position;
        var area = Screen.FromPoint(pos).WorkingArea;
        _maxPopupHeight = Math.Max(MinimumSize.Height, (int)(area.Height * 0.70));
        MaximumSize = new Size(PopupWidth, _maxPopupHeight);
        ResizeForContent();
        var x = Math.Min(Math.Max(area.Left, pos.X - Width + 16), area.Right - Width);
        var y = Math.Min(Math.Max(area.Top, pos.Y - Height - 8), area.Bottom - Height);
        Location = new Point(x, y);
        Show();
        Activate();
    }

    public void SetPinned(bool pinned, bool raiseEvent = true)
    {
        if (_pin.Checked == pinned)
        {
            ApplyPinnedMode();
            return;
        }

        var previous = _suppressPinEvent;
        _suppressPinEvent = !raiseEvent;
        try
        {
            _pin.Checked = pinned;
        }
        finally
        {
            _suppressPinEvent = previous;
        }
    }

    public void ApplyFloatingLocation(int? x, int? y)
    {
        if (x is null || y is null)
        {
            return;
        }

        var candidate = new Point(x.Value, y.Value);
        var area = Screen.FromPoint(candidate).WorkingArea;
        var clamped = new Point(
            Math.Min(Math.Max(area.Left, candidate.X), Math.Max(area.Left, area.Right - Width)),
            Math.Min(Math.Max(area.Top, candidate.Y), Math.Max(area.Top, area.Bottom - Height)));
        _restoringLocation = true;
        try
        {
            Location = clamped;
        }
        finally
        {
            _restoringLocation = false;
        }
    }

    private void ApplyPinnedMode()
    {
        ShowInTaskbar = IsPinned;
        TopMost = true;
        Text = IsPinned ? "BuildnBits Usage (pinned)" : "BuildnBits Usage";
        if (IsPinned)
        {
            FormBorderStyle = FormBorderStyle.SizableToolWindow;
            // MaximumSize is the outer window size. Keeping the borderless popup's width cap
            // after the tool-window chrome appears crushes the client area on refresh.
            var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
            if (IsHandleCreated)
            {
                area = Screen.FromControl(this).WorkingArea;
            }

            var chromeX = Math.Max(0, Width - ClientSize.Width);
            var chromeY = Math.Max(0, Height - ClientSize.Height);
            MaximumSize = new Size(area.Width, area.Height);
            MinimumSize = new Size(PopupWidth + chromeX, 180 + chromeY);
            if (ClientSize.Width < PopupWidth)
            {
                ClientSize = new Size(PopupWidth, Math.Max(ClientSize.Height, 180));
            }

            ResizeForContent();
            if (!Visible)
            {
                Show();
            }

            Activate();
        }
        else
        {
            FormBorderStyle = FormBorderStyle.None;
            MinimumSize = new Size(PopupWidth, 180);
            MaximumSize = new Size(PopupWidth, _maxPopupHeight);
        }
    }

    private static void AddOrderedRows(ProviderSection section, IEnumerable<UsageWindow> windows)
    {
        foreach (var window in windows
                     .OrderBy(window => window.ResetsAtUtc ?? DateTimeOffset.MaxValue)
                     .ThenBy(window => window.Label, StringComparer.OrdinalIgnoreCase))
        {
            section.AddRow(UsageLabel.Display(window.Label), window);
        }
    }

    private ProviderSection BuildCodexSection(ProviderSnapshot snapshot)
    {
        var section = new ProviderSection("Codex", snapshot, UsageIconRenderer.CodexColor);
        var windows = WindowsForPopup(ProviderKind.Codex, snapshot);
        if (HasUsageRows(snapshot) && windows.Count > 0)
        {
            AddOrderedRows(section, windows);
        }

        section.Finish();
        return section;
    }

    private ProviderSection BuildGrokSection(ProviderSnapshot snapshot)
    {
        var section = new ProviderSection("Grok", snapshot, UsageIconRenderer.GrokColor);
        var windows = WindowsForPopup(ProviderKind.Grok, snapshot);
        if (HasUsageRows(snapshot) && windows.Count > 0)
        {
            AddOrderedRows(section, windows);
        }

        section.Finish();
        return section;
    }

    private ProviderSection BuildAgySection(ProviderSnapshot snapshot)
    {
        var section = new ProviderSection("Antigravity", snapshot, UsageIconRenderer.AgyColor);
        var windows = WindowsForPopup(ProviderKind.Agy, snapshot);
        if (HasUsageRows(snapshot) && windows.Count > 0)
        {
            AddOrderedRows(section, windows);
        }

        section.Finish();
        return section;
    }

    private static bool HasUsageRows(ProviderSnapshot snapshot) =>
        snapshot.Windows.Count > 0 &&
        snapshot.Status is not UsageStatus.MissingCli and
        not UsageStatus.Unauthenticated and
        not UsageStatus.Unknown;

    private void ClearSections()
    {
        foreach (Control control in _providers.Controls)
        {
            control.Dispose();
        }

        _providers.Controls.Clear();
        _sections.Clear();
    }

    private void ApplyTheme()
    {
        var highContrast = SystemInformation.HighContrast;
        var dark = !highContrast && !IsLightTheme();
        BackColor = highContrast
            ? SystemColors.Window
            : dark ? Color.FromArgb(32, 32, 36) : Color.FromArgb(248, 248, 250);
        ForeColor = highContrast
            ? SystemColors.WindowText
            : dark ? Color.White : Color.FromArgb(20, 20, 24);
        _status.ForeColor = highContrast
            ? SystemColors.GrayText
            : dark ? Color.FromArgb(180, 180, 186) : Color.FromArgb(90, 90, 96);
        _providers.BackColor = BackColor;
        _layout.BackColor = BackColor;
        _launch.ForeColor = ForeColor;
        _pin.ForeColor = ForeColor;
        foreach (var section in _sections)
        {
            section.ApplyTheme(dark, highContrast);
        }
    }

    private void ResizeForContent()
    {
        SuspendLayout();
        try
        {
            _layout.PerformLayout();
            var providerHeight = _sections.Sum(section => section.Height + section.Margin.Vertical) + 2;
            var statusHeight = _status.GetPreferredSize(new Size(PopupWidth - Padding.Horizontal, 0)).Height;
            var actionsHeight = _layout.GetControlFromPosition(0, 2)?.GetPreferredSize(
                new Size(PopupWidth - Padding.Horizontal, 0)).Height ?? 36;
            var desired = Padding.Vertical + providerHeight + statusHeight + actionsHeight + 8;

            var maxClientHeight = _maxPopupHeight;
            if (IsPinned)
            {
                var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 720);
                if (IsHandleCreated)
                {
                    area = Screen.FromControl(this).WorkingArea;
                }

                var chromeY = Math.Max(0, Height - ClientSize.Height);
                maxClientHeight = Math.Max(200, area.Height - chromeY);
            }

            var desiredHeight = Math.Clamp(desired, 180, maxClientHeight);
            if (IsPinned)
            {
                var width = Math.Max(PopupWidth, ClientSize.Width);
                // Fit content after rebuilds; keep a taller size only if the user resized it up.
                var height = ClientSize.Height > desiredHeight + 24
                    ? ClientSize.Height
                    : desiredHeight;
                ClientSize = new Size(width, height);
            }
            else
            {
                ClientSize = new Size(PopupWidth, desiredHeight);
            }

            _layout.PerformLayout();
        }
        finally
        {
            ResumeLayout(true);
        }
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

    private static bool IsLightTheme()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 1;
        }
        catch
        {
            return false;
        }
    }

    private static Button StyledButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        Padding = new Padding(8, 3, 8, 3),
        Margin = new Padding(0, 0, 8, 6)
    };

    protected override void OnLostFocus(EventArgs e)
    {
        base.OnLostFocus(e);
        if (!IsPinned)
        {
            Hide();
        }
    }
}

internal sealed class ProviderSection : UserControl
{
    private const int HeaderHeight = 24;
    private readonly Label _header;
    private readonly Label _status;
    private readonly FlowLayoutPanel _rows;
    private readonly Color _accent;
    private readonly ProviderSnapshot _snapshot;
    private bool _hasRows;

    public ProviderSection(
        string title,
        ProviderSnapshot snapshot,
        Color accent)
    {
        _snapshot = snapshot;
        _accent = accent;
        AutoSize = false;
        Width = 406;
        Height = HeaderHeight + 28;
        Margin = new Padding(0, 0, 0, 6);
        Padding = new Padding(0);

        var plan = string.IsNullOrWhiteSpace(snapshot.PlanLabel) ? string.Empty : $" · {snapshot.PlanLabel}";
        _header = new Label
        {
            Text = title + plan,
            AutoEllipsis = true,
            AutoSize = false,
            Dock = DockStyle.Top,
            Width = Width,
            Height = HeaderHeight,
            Font = new Font("Segoe UI Semibold", 10f, FontStyle.Bold),
            Padding = new Padding(2, 2, 2, 0)
        };
        _rows = new FlowLayoutPanel
        {
            AutoSize = false,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Top,
            Width = Width,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        _status = new Label
        {
            AutoSize = false,
            Width = Width,
            Height = 28,
            Padding = new Padding(4, 3, 4, 2),
            Text = StatusText(snapshot),
            Visible = snapshot.Windows.Count == 0 ||
                      snapshot.Status is not UsageStatus.Ok
        };
        _rows.Controls.Add(_status);
        Controls.Add(_rows);
        Controls.Add(_header);
    }

    public void AddRow(string label, UsageWindow? window)
    {
        _hasRows = true;
        var row = new UsageRow(label, _accent);
        row.Set(window, DateTimeOffset.UtcNow);
        _rows.Controls.Add(row);
    }

    public void Finish()
    {
        _status.Visible = !_hasRows || _snapshot.Status is not UsageStatus.Ok;
        var rowsHeight = _rows.Controls
            .Cast<Control>()
            .Where(control => control.Visible)
            .Sum(control => control.Height + control.Margin.Vertical);
        _rows.Height = Math.Max(1, rowsHeight);
        Height = _header.Height + _rows.Height;
    }

    public void ApplyTheme(bool dark, bool highContrast)
    {
        BackColor = highContrast
            ? SystemColors.Window
            : dark ? Color.FromArgb(32, 32, 36) : Color.FromArgb(248, 248, 250);
        ForeColor = highContrast
            ? SystemColors.WindowText
            : dark ? Color.White : Color.FromArgb(20, 20, 24);
        _header.ForeColor = highContrast ? SystemColors.WindowText : _accent;
        _status.ForeColor = highContrast
            ? SystemColors.GrayText
            : dark ? Color.FromArgb(180, 180, 186) : Color.FromArgb(90, 90, 96);
        _status.BackColor = BackColor;
        _rows.BackColor = BackColor;
        foreach (Control control in _rows.Controls)
        {
            if (control is UsageRow row)
            {
                row.ApplyTheme(dark, highContrast);
            }
        }
    }

    private static string StatusText(ProviderSnapshot snapshot)
    {
        if (!string.IsNullOrWhiteSpace(snapshot.StatusMessage))
        {
            return AppLog.Sanitize(snapshot.StatusMessage);
        }

        return snapshot.Status switch
        {
            UsageStatus.MissingCli => "CLI not found.",
            UsageStatus.Unauthenticated => "Sign in with the provider CLI to show usage.",
            UsageStatus.AuthRejected => "This authentication method is not supported.",
            UsageStatus.Stale => "Showing the last successful values.",
            UsageStatus.Error => "Provider request failed.",
            UsageStatus.Unknown => "Waiting for the first refresh.",
            _ => "Up to date."
        };
    }
}

internal sealed class UsageRow : UserControl
{
    private readonly Label _label;
    private readonly Label _percent;
    private readonly Label _caption;
    private readonly Color _accent;
    private readonly ToolTip _toolTip = new();
    private int _remaining;
    private bool _highContrast;

    public UsageRow(string label, Color accent)
    {
        _accent = accent;
        // Tall enough for the percent glyphs above the bottom progress bar.
        Height = 36;
        Width = 406;
        Margin = new Padding(0, 0, 0, 3);
        Padding = new Padding(6, 3, 6, 8);
        _label = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Text = label,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Font = new Font("Segoe UI", 8.75f)
        };
        _percent = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleRight,
            // Semibold family + Bold style was optically too tall for the row.
            Font = new Font("Segoe UI Semibold", 12f, FontStyle.Regular),
            Width = 56,
            UseMnemonic = false
        };
        _caption = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            Font = new Font("Segoe UI", 8f),
            Width = 134
        };
        var content = new TableLayoutPanel
        {
            ColumnCount = 3,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            Padding = new Padding(0),
            BackColor = Color.Transparent
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 134));
        content.Controls.Add(_label, 0, 0);
        content.Controls.Add(_percent, 1, 0);
        content.Controls.Add(_caption, 2, 0);
        Controls.Add(content);
        SetStyle(
            ControlStyles.ResizeRedraw |
            ControlStyles.UserPaint |
            ControlStyles.AllPaintingInWmPaint |
            ControlStyles.OptimizedDoubleBuffer,
            true);
    }

    public void Set(UsageWindow? window, DateTimeOffset nowUtc)
    {
        _remaining = window is null ? 0 : PercentageMath.DisplayPercent(window.RemainingPercent);
        _percent.Text = window is null ? "—" : $"{_remaining}%";
        _caption.Text = window is null ? "unavailable" : ResetCountdown.Caption(window.ResetsAtUtc, nowUtc);
        if (window is not null)
        {
            _toolTip.SetToolTip(this, window.Label);
            _toolTip.SetToolTip(_label, window.Label);
        }

        Invalidate();
    }

    public void ApplyTheme(bool dark, bool highContrast)
    {
        _highContrast = highContrast;
        var background = highContrast
            ? SystemColors.Window
            : dark ? Color.FromArgb(42, 42, 48) : Color.FromArgb(238, 238, 242);
        BackColor = background;
        ForeColor = highContrast
            ? SystemColors.WindowText
            : dark ? Color.White : Color.FromArgb(20, 20, 24);
        _label.ForeColor = ForeColor;
        _percent.ForeColor = ForeColor;
        _caption.ForeColor = highContrast
            ? SystemColors.GrayText
            : dark ? Color.FromArgb(190, 190, 196) : Color.FromArgb(90, 90, 96);
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var fill = new SolidBrush(_highContrast ? SystemColors.Window : Color.FromArgb(24, _accent));
        e.Graphics.FillRectangle(fill, bounds);
        using var border = new Pen(_highContrast ? SystemColors.WindowText : Color.FromArgb(50, _accent));
        e.Graphics.DrawRectangle(border, bounds);
        var bar = new Rectangle(6, Height - 6, Math.Max(1, Width - 12), 3);
        using var track = new SolidBrush(_highContrast ? SystemColors.WindowText : Color.FromArgb(55, _accent));
        e.Graphics.FillRectangle(track, bar);
        using var value = new SolidBrush(_highContrast ? SystemColors.WindowText : _accent);
        var width = (int)(bar.Width * Math.Clamp(_remaining, 0, 100) / 100f);
        e.Graphics.FillRectangle(value, bar.X, bar.Y, width, bar.Height);
    }
}
