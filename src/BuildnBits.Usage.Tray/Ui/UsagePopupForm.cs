using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;
using BuildnBits.Usage.Core.Providers.Codex;
using BuildnBits.Usage.Tray.Icons;

namespace BuildnBits.Usage.Tray.Ui;

public sealed class UsagePopupForm : Form
{
    private readonly UsageCard _codexFive = new("Codex 5-hour", UsageIconRenderer.CodexColor);
    private readonly UsageCard _codexWeek = new("Codex 7-day", UsageIconRenderer.CodexColor);
    private readonly UsageCard _grokWeek = new("Grok weekly", UsageIconRenderer.GrokColor);
    private readonly UsageCard _agyQuota = new("Google Antigravity [agy]", UsageIconRenderer.AgyColor);
    private readonly Label _status = new();
    private readonly Button _refresh = StyledButton("Refresh");
    private readonly Button _settings = StyledButton("Settings");
    private readonly Button _diagnostics = StyledButton("Diagnostics");
    private readonly CheckBox _launch = new() { Text = "Launch at login", AutoSize = true, FlatStyle = FlatStyle.Flat };
    private readonly Button _exit = StyledButton("Exit");

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
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(12);
        BackColor = Color.FromArgb(32, 32, 36);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 9f);

        var layout = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 6,
            Dock = DockStyle.Fill,
            Padding = new Padding(0)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 336));
        foreach (var card in new[] { _codexFive, _codexWeek, _grokWeek, _agyQuota })
        {
            card.Margin = new Padding(0, 0, 0, 8);
            layout.Controls.Add(card);
        }

        _status.AutoSize = true;
        _status.Margin = new Padding(0, 4, 0, 8);
        _status.MaximumSize = new Size(336, 0);
        layout.Controls.Add(_status);

        var actions = new FlowLayoutPanel
        {
            AutoSize = true,
            WrapContents = true,
            Width = 336,
            Margin = new Padding(0)
        };
        actions.Controls.Add(_refresh);
        actions.Controls.Add(_settings);
        actions.Controls.Add(_diagnostics);
        actions.Controls.Add(_launch);
        actions.Controls.Add(_exit);
        layout.Controls.Add(actions);
        Controls.Add(layout);

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
        ApplyTheme();
        var now = DateTimeOffset.UtcNow;
        BindCard(_codexFive, state.Codex.WindowByDuration(CodexWindowDurations.FiveHourMinutes), now);
        BindCard(_codexWeek, state.Codex.WindowByDuration(CodexWindowDurations.SevenDayMinutes), now);
        BindCard(_grokWeek, state.Grok.Weekly ?? state.Grok.Windows.FirstOrDefault(), now);
        BindCard(_agyQuota, state.Agy.Windows.OrderBy(w => w.RemainingPercent).FirstOrDefault(), now);

        var last = state.LastSuccessfulRefreshUtc?.ToLocalTime().ToString("t") ?? "never";
        var stale = state.Codex.Status is UsageStatus.Stale or UsageStatus.Error ||
                    state.Grok.Status is UsageStatus.Stale or UsageStatus.Error ||
                    state.Agy.Status is UsageStatus.Stale or UsageStatus.Error;
        var issueSnapshot = new[] { state.Codex, state.Grok, state.Agy }
            .FirstOrDefault(s => s.Status is not UsageStatus.Ok and not UsageStatus.Unknown);
        var detail = issueSnapshot?.StatusMessage ?? (stale
            ? "Showing last successful values."
            : "Up to date.");
        _status.Text = $"Updated {last} · Codex {state.Codex.Status} · Grok {state.Grok.Status} · agy {state.Agy.Status}\n{detail}";
        _launch.Checked = launchAtLogin;
    }

    public void ShowNearCursor()
    {
        var pos = Cursor.Position;
        var area = Screen.FromPoint(pos).WorkingArea;
        var x = Math.Min(Math.Max(area.Left, pos.X - Width + 16), area.Right - Width);
        var y = Math.Min(Math.Max(area.Top, pos.Y - Height - 8), area.Bottom - Height);
        Location = new Point(x, y);
        Show();
        Activate();
    }

    private void ApplyTheme()
    {
        if (SystemInformation.HighContrast)
        {
            BackColor = SystemColors.Window;
            ForeColor = SystemColors.WindowText;
            _status.ForeColor = SystemColors.GrayText;
            _launch.ForeColor = SystemColors.WindowText;
            foreach (var card in new[] { _codexFive, _codexWeek, _grokWeek, _agyQuota })
            {
                card.ForeColor = ForeColor;
                card.BackColor = BackColor;
            }

            return;
        }

        var dark = !IsLightTheme();
        BackColor = dark ? Color.FromArgb(32, 32, 36) : Color.FromArgb(248, 248, 250);
        ForeColor = dark ? Color.White : Color.FromArgb(20, 20, 24);
        _status.ForeColor = dark ? Color.FromArgb(180, 180, 186) : Color.FromArgb(90, 90, 96);
        _launch.ForeColor = ForeColor;
        foreach (var card in new[] { _codexFive, _codexWeek, _grokWeek, _agyQuota })
        {
            card.ForeColor = ForeColor;
            card.BackColor = BackColor;
        }
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

    private static void BindCard(UsageCard card, UsageWindow? window, DateTimeOffset now)
    {
        if (window is null)
        {
            card.Set(null, "unavailable", "—");
            return;
        }

        card.Set(
            PercentageMath.DisplayPercent(window.RemainingPercent),
            ResetCountdown.Format(ResetCountdown.Remaining(window.ResetsAtUtc, now)),
            ResetCountdown.LocalResetLabel(window.ResetsAtUtc));
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

public sealed class UsageCard : UserControl
{
    private readonly Label _title;
    private readonly Label _percent;
    private readonly Label _caption;
    private readonly Color _accent;
    private int _remaining;

    public UsageCard(string title, Color accent)
    {
        _accent = accent;
        Size = new Size(336, 108);
        MinimumSize = new Size(336, 108);
        Padding = new Padding(12, 10, 12, 16);
        _title = new Label
        {
            Text = title,
            AutoSize = true,
            Font = new Font("Segoe UI", 11f),
            Dock = DockStyle.Top,
            Padding = new Padding(0, 0, 0, 4)
        };
        _percent = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Left,
            Width = 92,
            Font = new Font("Segoe UI Semibold", 22f, FontStyle.Bold),
            TextAlign = ContentAlignment.MiddleLeft
        };
        _caption = new Label
        {
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font("Segoe UI", 9f),
            TextAlign = ContentAlignment.MiddleLeft
        };
        var row = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 0, 0, 10) };
        row.Controls.Add(_caption);
        row.Controls.Add(_percent);
        Controls.Add(row);
        Controls.Add(_title);
        SetStyle(ControlStyles.ResizeRedraw | ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint, true);
    }

    protected override void OnForeColorChanged(EventArgs e)
    {
        base.OnForeColorChanged(e);
        _title.ForeColor = ForeColor;
        _percent.ForeColor = ForeColor;
        _caption.ForeColor = ForeColor;
    }

    public void Set(int? remaining, string countdown, string reset)
    {
        _remaining = remaining ?? 0;
        _percent.Text = remaining is null ? "—" : $"{remaining}%";
        _caption.Text = $"{countdown}{Environment.NewLine}{reset}";
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        var bounds = new Rectangle(0, 0, Width - 1, Height - 1);
        using var fill = new SolidBrush(Color.FromArgb(22, _accent));
        g.FillRectangle(fill, bounds);
        using var border = new Pen(Color.FromArgb(50, _accent));
        g.DrawRectangle(border, bounds);
        var bar = new Rectangle(Padding.Left, Height - 12, Width - Padding.Horizontal, 5);
        using var track = new SolidBrush(Color.FromArgb(50, _accent));
        g.FillRectangle(track, bar);
        using var value = new SolidBrush(_accent);
        var width = (int)(bar.Width * Math.Clamp(_remaining, 0, 100) / 100f);
        g.FillRectangle(value, bar.X, bar.Y, width, bar.Height);
    }
}
