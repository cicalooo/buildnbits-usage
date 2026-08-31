using System.Text.Json;
using BuildnBits.Usage.Core.Parsing;
using BuildnBits.Usage.Core.Providers.Codex;

namespace BuildnBits.Usage.Tests;

public class CodexParsingTests
{
    [Fact]
    public void Parses_five_hour_and_seven_day_windows()
    {
        const string json = """
            {
              "rateLimits": {
                "limitId": "codex",
                "planType": "plus",
                "primary": { "usedPercent": 25, "windowDurationMins": 300, "resetsAt": 1730947200 },
                "secondary": { "usedPercent": 10, "windowDurationMins": 10080, "resetsAt": 1731552000 }
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = CodexRateLimitsParser.Parse(doc.RootElement, DateTimeOffset.FromUnixTimeSeconds(1730940000));
        Assert.Equal("plus", snapshot.PlanLabel);
        var five = snapshot.WindowByDuration(300)!;
        var week = snapshot.WindowByDuration(10080)!;
        Assert.Equal(75, five.RemainingPercent);
        Assert.Equal(90, week.RemainingPercent);
        Assert.Equal(75, PercentageMath.LowestRemaining([five.RemainingPercent, week.RemainingPercent]));
        Assert.Equal("5-hour", five.Label);
        Assert.Equal("7-day", week.Label);
    }

    [Fact]
    public void Uses_rateLimitsByLimitId_codex_bucket()
    {
        const string json = """
            {
              "rateLimitsByLimitId": {
                "codex": {
                  "primary": { "usedPercent": 40, "windowDurationMins": 300, "resetsAt": 1730947200 },
                  "secondary": { "usedPercent": 5, "windowDurationMins": 10080, "resetsAt": 1731552000 }
                },
                "codex_other": {
                  "primary": { "usedPercent": 99, "windowDurationMins": 15, "resetsAt": 1730940800 }
                }
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = CodexRateLimitsParser.Parse(doc.RootElement, DateTimeOffset.UtcNow);
        Assert.Equal(60, snapshot.WindowByDuration(300)!.RemainingPercent);
        Assert.Equal(1, snapshot.WindowByDuration(15)!.RemainingPercent);
    }

    [Fact]
    public void Rejects_api_key_account()
    {
        const string json = """
            { "account": { "type": "apikey" }, "requiresOpenaiAuth": true }
            """;
        using var doc = JsonDocument.Parse(json);
        Assert.Throws<CodexApiKeyRejectedException>(() => CodexRateLimitsParser.AssertChatGptAuth(doc.RootElement));
    }

    [Fact]
    public void Reset_countdown_uses_utc_then_local_display()
    {
        var resets = new DateTimeOffset(2026, 1, 2, 15, 0, 0, TimeSpan.Zero);
        var now = new DateTimeOffset(2026, 1, 2, 14, 0, 0, TimeSpan.Zero);
        var remaining = ResetCountdown.Remaining(resets, now);
        Assert.Equal(TimeSpan.FromHours(1), remaining);
        Assert.Contains("remaining", ResetCountdown.Format(remaining));
        Assert.StartsWith("resets", ResetCountdown.LocalResetLabel(resets));
    }
}
