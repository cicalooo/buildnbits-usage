using System.Drawing.Drawing2D;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;
using BuildnBits.Usage.Core.Providers.Codex;
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
    private readonly Button _exit = StyledButton("Exit");
    private readonly List<ProviderSection> _sections = [];
    private int _maxPopupHeight = DefaultMaxHeight;

    public event EventHandler? RefreshClicked;
    public event EventHandler? SettingsClicked;
    public event EventHandler? DiagnosticsClicked;
    public event EventHandler? ExitClicked;
    public event EventHandler<bool>? LaunchAtLoginChanged;

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
        actions.Controls.Add(_exit);
        _layout.Controls.Add(actions, 0, 2);
        Controls.Add(_layout);

        _refresh.Click += (_, _) => RefreshClicked?.Invoke(this, EventArgs.Empty);
        _settings.Click += (_, _) => SettingsClicked?.Invoke(this, EventArgs.Empty);
        _diagnostics.Click += (_, _) => DiagnosticsClicked?.Invoke(this, EventArgs.Empty);
        _exit.Click += (_, _) => ExitClicked?.Invoke(this, EventArgs.Empty);
        _launch.CheckedChanged += (_, _) => LaunchAtLoginChanged?.Invoke(this, _launch.Checked);
        Deactivate += (_, _) => Hide();
        Paint += (_, e) =>
        {
            using var pen = new Pen(Color.FromArgb(70, 70, 76));
            e.Graphics.DrawRectangle(pen, 0, 0, Width - 1, Height - 1);
        };
    }

    public void Bind(CombinedUsageState state, bool launchAtLogin)
    {
        ClearSections();
        _sections.Add(BuildCodexSection(state.Codex));
        _sections.Add(BuildGrokSection(state.Grok));
        _sections.Add(BuildAgySection(state.Agy));
        foreach (var section in _sections)
        {
            _providers.Controls.Add(section);
        }

        var last = state.LastSuccessfulRefreshUtc;
        var issueSnapshot = new[] { state.Codex, state.Grok, state.Agy }
            .FirstOrDefault(snapshot => snapshot.Status is not UsageStatus.Ok);
        var stale = new[] { state.Codex, state.Grok, state.Agy }
            .Any(snapshot => snapshot.Status is UsageStatus.Stale or UsageStatus.Error);
        var detail = issueSnapshot?.StatusMessage is { } message
            ? AppLog.Sanitize(message)
            : issueSnapshot?.Status is UsageStatus.Unknown
                ? "Waiting for the first refresh."
                : stale ? "Showing last successful values." : "Up to date.";
        _status.Text =
            $"{RefreshAge(last)} · Codex {state.Codex.Status} · Grok {state.Grok.Status} · " +
            $"Antigravity {state.Agy.Status}{Environment.NewLine}{detail}";
        _launch.Checked = launchAtLogin;
        ApplyTheme();
        ResizeForContent();
    }

    public void ShowNearCursor()
    {
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

    private ProviderSection BuildCodexSection(ProviderSnapshot snapshot)
    {
        var section = new ProviderSection("Codex", snapshot, UsageIconRenderer.CodexColor);
        if (HasUsageRows(snapshot))
        {
            section.AddRow("5-hour", snapshot.WindowByDuration(CodexWindowDurations.FiveHourMinutes));
            section.AddRow("7-day", snapshot.WindowByDuration(CodexWindowDurations.SevenDayMinutes));
            foreach (var window in snapshot.Windows.Where(window =>
                         window.DurationMinutes is not CodexWindowDurations.FiveHourMinutes and
                         not CodexWindowDurations.SevenDayMinutes))
            {
                section.AddRow(window.Label, window);
            }
        }

        section.Finish();
        return section;
    }

    private static ProviderSection BuildGrokSection(ProviderSnapshot snapshot)
    {
        var section = new ProviderSection("Grok", snapshot, UsageIconRenderer.GrokColor);
        if (HasUsageRows(snapshot))
        {
            section.AddRow("Weekly", snapshot.Weekly ?? snapshot.Windows.FirstOrDefault());
        }

        section.Finish();
        return section;
    }

    private static ProviderSection BuildAgySection(ProviderSnapshot snapshot)
    {
        var section = new ProviderSection("Antigravity", snapshot, UsageIconRenderer.AgyColor);
        if (HasUsageRows(snapshot))
        {
            foreach (var window in snapshot.Windows)
            {
                section.AddRow(window.Label, window);
            }
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
        foreach (var section in _sections)
        {
            section.ApplyTheme(dark, highContrast);
        }
    }

    private void ResizeForContent()
    {
        var providerHeight = _sections.Sum(section => section.Height + section.Margin.Vertical) + 2;
        var statusHeight = _status.GetPreferredSize(new Size(PopupWidth - Padding.Horizontal, 0)).Height;
        var actionsHeight = _layout.GetControlFromPosition(0, 2)?.GetPreferredSize(
            new Size(PopupWidth - Padding.Horizontal, 0)).Height ?? 36;
        var desired = Padding.Vertical + providerHeight + statusHeight + actionsHeight + 8;
        ClientSize = new Size(PopupWidth, Math.Clamp(desired, MinimumSize.Height, _maxPopupHeight));
        _layout.PerformLayout();
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
        Hide();
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
        Height = 34;
        Width = 406;
        Margin = new Padding(0, 0, 0, 3);
        Padding = new Padding(6, 1, 6, 6);
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
            Font = new Font("Segoe UI Semibold", 14f, FontStyle.Bold),
            Width = 52
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
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 52));
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
        _caption.Text = window is null
            ? "unavailable"
            : $"{ResetCountdown.Format(ResetCountdown.Remaining(window.ResetsAtUtc, nowUtc))}\n" +
              ResetCountdown.LocalResetLabel(window.ResetsAtUtc);
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
        var bar = new Rectangle(6, Height - 5, Math.Max(1, Width - 12), 3);
        using var track = new SolidBrush(_highContrast ? SystemColors.WindowText : Color.FromArgb(55, _accent));
        e.Graphics.FillRectangle(track, bar);
        using var value = new SolidBrush(_highContrast ? SystemColors.WindowText : _accent);
        var width = (int)(bar.Width * Math.Clamp(_remaining, 0, 100) / 100f);
        e.Graphics.FillRectangle(value, bar.X, bar.Y, width, bar.Height);
    }
}
