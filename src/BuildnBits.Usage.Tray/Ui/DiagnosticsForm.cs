using System.Diagnostics;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Tray.Ui;

public sealed class DiagnosticsForm : Form
{
    private readonly AppLog? _log;

    public DiagnosticsForm(CombinedUsageState state, AppLog? log = null)
    {
        _log = log;
        Text = "Diagnostics";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(620, 390);

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9),
            Text = Format(state, log)
        };

        var openLog = new Button
        {
            Text = "Open log",
            AutoSize = true,
            Enabled = log is not null
        };
        openLog.Click += (_, _) => OpenLog();

        var copyLog = new Button
        {
            Text = "Copy recent log",
            AutoSize = true,
            Enabled = log is not null
        };
        copyLog.Click += (_, _) => CopyRecentLog();

        var copyDiagnostics = new Button
        {
            Text = "Copy diagnostics",
            AutoSize = true
        };
        copyDiagnostics.Click += (_, _) => CopyText(box.Text);

        var actions = new FlowLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            WrapContents = false,
            Padding = new Padding(4)
        };
        actions.Controls.Add(openLog);
        actions.Controls.Add(copyLog);
        actions.Controls.Add(copyDiagnostics);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(actions, 0, 0);
        layout.Controls.Add(box, 0, 1);
        Controls.Add(layout);
    }

    private void OpenLog()
    {
        if (_log is null)
        {
            return;
        }

        try
        {
            _log.EnsureFile();
            Process.Start(new ProcessStartInfo
            {
                FileName = _log.PathOnDisk,
                UseShellExecute = true
            });
        }
        catch
        {
            // Opening a viewer is a convenience and may be unavailable in
            // restricted desktop environments.
        }
    }

    private void CopyRecentLog()
    {
        if (_log is null)
        {
            return;
        }

        try
        {
            CopyText(_log.ReadRecent());
        }
        catch
        {
            // Clipboard access can be denied by another desktop session.
        }
    }

    private static void CopyText(string value)
    {
        try
        {
            Clipboard.SetText(value);
        }
        catch
        {
            // Clipboard access can be denied by another desktop session.
        }
    }

    private static string Format(CombinedUsageState state, AppLog? log)
    {
        var logPath = log is null ? string.Empty : $"Log: {log.PathOnDisk}";
        return string.Join(Environment.NewLine, [
            $"Last attempt (UTC): {state.LastAttemptUtc:O}",
            $"Last success (UTC): {state.LastSuccessfulRefreshUtc:O}",
            logPath,
            FormatProvider(state.Codex),
            FormatProvider(state.Grok),
            FormatProvider(state.Agy),
            "Credentials, tokens, cookies, and prompts are never copied here."
        ]);
    }

    private static string FormatProvider(ProviderSnapshot snapshot)
    {
        var windows = string.Join("; ", snapshot.Windows.Select(w =>
            $"{AppLog.Sanitize(w.Label)} remaining={w.RemainingPercent:0} used={w.UsedPercent:0} duration={w.DurationMinutes} resets={w.ResetsAtUtc:O}"));
        var plan = string.IsNullOrWhiteSpace(snapshot.PlanLabel) ? "unknown" : AppLog.Sanitize(snapshot.PlanLabel);
        var message = string.IsNullOrWhiteSpace(snapshot.StatusMessage) ? "none" : AppLog.Sanitize(snapshot.StatusMessage);
        return $"{snapshot.Provider}: status={snapshot.Status} plan={plan} fetched={snapshot.FetchedAtUtc:O} msg={message} windows=[{windows}]";
    }
}
