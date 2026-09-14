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
              "text": "Grok ${grokBuildRemaining}% build · ${grokBotRemaining}% bot",
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
                  "text": "5h ${codexFiveRemaining}% · ${codexFiveCountdown}",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": "7d ${codexWeekRemaining}% · ${codexWeekCountdown}",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": "Grok ${grokPlan}",
                  "weight": "Bolder"
                },
                {
                  "type": "TextBlock",
                  "text": "Build ${grokBuildRemaining}% · ${grokBuildCountdown}",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": "Bot ${grokBotRemaining}% · ${grokBotCountdown}",
                  "wrap": true
                },
                {
                  "type": "TextBlock",
                  "text": "Google Antigravity [agy] ${agyPlan}",
                  "weight": "Bolder"
                },
                {
                  "type": "TextBlock",
                  "text": "Lowest ${agyRemaining}% · ${agyCountdown}",
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
        var grokBuild = state.Grok.Windows.FirstOrDefault(w =>
                            string.Equals(w.Label, "Build", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(w.Label, "Weekly", StringComparison.OrdinalIgnoreCase));
        var grokBot = state.Grok.Windows.FirstOrDefault(w =>
            string.Equals(w.Label, "Bot", StringComparison.OrdinalIgnoreCase));
        var agy = state.Agy.Windows.OrderBy(w => w.RemainingPercent).FirstOrDefault();
        var agyPools = state.Agy.Windows.Count == 0
            ? "unavailable"
            : string.Join(" · ", state.Agy.Windows.Select(w =>
                $"{UsageLabel.Display(w.Label)} {PercentageMath.DisplayPercent(w.RemainingPercent)}%"));
        var data = new JsonObject
        {
            ["codexPlan"] = state.Codex.PlanLabel ?? "Codex",
            ["codexFiveRemaining"] = Display(five),
            ["codexFiveCountdown"] = ResetCountdown.Caption(five?.ResetsAtUtc, nowUtc),
            ["codexWeekRemaining"] = Display(week),
            ["codexWeekCountdown"] = ResetCountdown.Caption(week?.ResetsAtUtc, nowUtc),
            ["grokPlan"] = state.Grok.PlanLabel ?? "Grok",
            ["grokRemaining"] = Display(grokBuild),
            ["grokCountdown"] = ResetCountdown.Caption(grokBuild?.ResetsAtUtc, nowUtc),
            ["grokBuildRemaining"] = Display(grokBuild),
            ["grokBuildCountdown"] = ResetCountdown.Caption(grokBuild?.ResetsAtUtc, nowUtc),
            ["grokBotRemaining"] = Display(grokBot),
            ["grokBotCountdown"] = ResetCountdown.Caption(grokBot?.ResetsAtUtc, nowUtc),
            ["agyPlan"] = state.Agy.PlanLabel ?? "Antigravity",
            ["agyRemaining"] = Display(agy),
            ["agyCountdown"] = ResetCountdown.Caption(agy?.ResetsAtUtc, nowUtc),
            ["agyPools"] = agyPools,
            ["statusLine"] = $"Codex {Freshness(state.Codex)} {state.Codex.Status} · " +
                              $"Grok {Freshness(state.Grok)} {state.Grok.Status} · " +
                              $"agy {Freshness(state.Agy)} {state.Agy.Status}"
        };
        return data.ToJsonString(new JsonSerializerOptions { WriteIndented = false });
    }

    private static JsonNode Display(UsageWindow? window) =>
        window is null ? "—" : PercentageMath.DisplayPercent(window.RemainingPercent);

    private static string Freshness(ProviderSnapshot snapshot) =>
        snapshot.FetchedAtUtc is { } fetched
            ? fetched.ToLocalTime().ToString("g")
            : "never";
}
