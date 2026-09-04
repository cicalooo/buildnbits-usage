using BuildnBits.Usage.Core.Models;

namespace BuildnBits.Usage.Tray.Ui;

public sealed class DiagnosticsForm : Form
{
    public DiagnosticsForm(CombinedUsageState state)
    {
        Text = "Diagnostics";
        FormBorderStyle = FormBorderStyle.Sizable;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(520, 320);

        var box = new TextBox
        {
            Multiline = true,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            ScrollBars = ScrollBars.Both,
            Font = new Font("Consolas", 9),
            Text = Format(state)
        };
        Controls.Add(box);
    }

    private static string Format(CombinedUsageState state)
    {
        return string.Join(Environment.NewLine, [
            $"Last attempt (UTC): {state.LastAttemptUtc:O}",
            $"Last success (UTC): {state.LastSuccessfulRefreshUtc:O}",
            FormatProvider(state.Codex),
            FormatProvider(state.Grok),
            FormatProvider(state.Agy),
            "Credentials, tokens, cookies, and prompts are never copied here."
        ]);
    }

    private static string FormatProvider(ProviderSnapshot snapshot)
    {
        var windows = string.Join("; ", snapshot.Windows.Select(w =>
            $"{w.Label} remaining={w.RemainingPercent:0} used={w.UsedPercent:0} duration={w.DurationMinutes} resets={w.ResetsAtUtc:O}"));
        return $"{snapshot.Provider}: status={snapshot.Status} plan={snapshot.PlanLabel} fetched={snapshot.FetchedAtUtc:O} msg={snapshot.StatusMessage} windows=[{windows}]";
    }
}
