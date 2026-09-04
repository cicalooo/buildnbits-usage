using System.Text.Json;
using System.Text.Json.Nodes;
using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Parsing;
using BuildnBits.Usage.Core.Providers.Codex;

namespace BuildnBits.Usage.Core.Widgets;

public static class UsageAdaptiveCard
{
    public const string WidgetName = "BuildnBitsUsage";

    public static string TemplateJson() => """
        {
          "type": "AdaptiveCard",
          "version": "1.5",
          "$schema": "http://adaptivecards.io/schemas/adaptive-card.json",
          "body": [
            {
              "type": "TextBlock",
              "text": "BuildnBits Usage",
              "weight": "Bolder",
              "size": "Medium"
            },
            {
              "type": "TextBlock",
              "text": "Codex ${codexFiveRemaining}% 5h · ${codexWeekRemaining}% 7d",
              "$when": "${$host.widgetSize==\"small\"}",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "Grok ${grokRemaining}% weekly",
              "$when": "${$host.widgetSize==\"small\"}",
              "wrap": true
            },
            {
              "type": "TextBlock",
              "text": "Antigravity [agy] ${agyRemaining}% lowest weekly",
              "$when": "${$host.widgetSize==\"small\"}",
              "wrap": true
            },
            {
              "type": "Container",
              "$when": "${$host.widgetSize!=\"small\"}",
              "items": [
                {
                  "type": "TextBlock",
                  "text": "Codex ${codexPlan}",
                  "weight": "Bolder"
                },
                {
                  "type": "TextBlock",
                  "text": "5-hour ${codexFiveRemaining}% remaining · ${codexFiveCountdown}",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": "7-day ${codexWeekRemaining}% remaining · ${codexWeekCountdown}",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": "Grok ${grokPlan}",
                  "weight": "Bolder"
                },
                {
                  "type": "TextBlock",
                  "text": "Weekly ${grokRemaining}% remaining · ${grokCountdown}",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": "Google Antigravity [agy] ${agyPlan}",
                  "weight": "Bolder"
                },
                {
                  "type": "TextBlock",
                  "text": "Lowest weekly pool ${agyRemaining}% remaining · ${agyCountdown}",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": "Pools: ${agyPools}",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": "${statusLine}",
                  "isSubtle": true,
                  "wrap": true
                }
              ]
            }
          ],
          "actions": [
            {
              "type": "Action.Execute",
              "title": "Refresh",
              "verb": "refresh"
            },
            {
              "type": "Action.Execute",
              "title": "Open app",
              "verb": "openApp"
            }
          ]
        }
        """;

    public static string DataJson(CombinedUsageState state, DateTimeOffset nowUtc)
    {
        var five = state.Codex.WindowByDuration(CodexWindowDurations.FiveHourMinutes);
        var week = state.Codex.WindowByDuration(CodexWindowDurations.SevenDayMinutes);
        var grok = state.Grok.Weekly ?? state.Grok.Windows.FirstOrDefault();
        var agy = state.Agy.Windows.OrderBy(w => w.RemainingPercent).FirstOrDefault();
        var agyPools = state.Agy.Windows.Count == 0
            ? "unavailable"
            : string.Join(" · ", state.Agy.Windows.Select(w =>
                $"{w.Label} {PercentageMath.DisplayPercent(w.RemainingPercent)}%"));
        var data = new JsonObject
        {
            ["codexPlan"] = state.Codex.PlanLabel ?? "Codex",
            ["codexFiveRemaining"] = Display(five),
            ["codexFiveCountdown"] = ResetCountdown.Format(ResetCountdown.Remaining(five?.ResetsAtUtc, nowUtc)),
            ["codexWeekRemaining"] = Display(week),
            ["codexWeekCountdown"] = ResetCountdown.Format(ResetCountdown.Remaining(week?.ResetsAtUtc, nowUtc)),
            ["grokPlan"] = state.Grok.PlanLabel ?? "Grok",
            ["grokRemaining"] = Display(grok),
            ["grokCountdown"] = ResetCountdown.Format(ResetCountdown.Remaining(grok?.ResetsAtUtc, nowUtc)),
            ["agyPlan"] = state.Agy.PlanLabel ?? "Antigravity",
            ["agyRemaining"] = Display(agy),
            ["agyCountdown"] = ResetCountdown.Format(ResetCountdown.Remaining(agy?.ResetsAtUtc, nowUtc)),
            ["agyPools"] = agyPools,
            ["statusLine"] = $"Updated {state.LastSuccessfulRefreshUtc?.ToLocalTime():g} · Codex {state.Codex.Status} · Grok {state.Grok.Status} · agy {state.Agy.Status}"
        };
        return data.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonNode Display(UsageWindow? window) =>
        window is null ? "—" : PercentageMath.DisplayPercent(window.RemainingPercent);
}
