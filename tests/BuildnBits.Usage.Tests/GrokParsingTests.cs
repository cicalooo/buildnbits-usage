using System.Text.Json;
using BuildnBits.Usage.Core.Providers.Grok;

namespace BuildnBits.Usage.Tests;

public class GrokParsingTests
{
    [Fact]
    public void Parses_weekly_credits_config()
    {
        const string json = """
            {
              "config": {
                "creditUsagePercent": 42.5,
                "currentPeriod": {
                  "type": "USAGE_PERIOD_TYPE_WEEKLY",
                  "start": "2026-06-01T00:00:00Z",
                  "end": "2026-06-08T00:00:00Z"
                }
              },
              "subscriptionTier": "SuperGrok"
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = GrokBillingParser.Parse(doc.RootElement, DateTimeOffset.Parse("2026-06-02T00:00:00Z"));
        Assert.Equal("SuperGrok", snapshot.PlanLabel);
        Assert.Equal(58, snapshot.Weekly!.RemainingPercent);
        Assert.Equal(new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero), snapshot.Weekly.ResetsAtUtc);
    }

    [Fact]
    public void Falls_back_to_legacy_cents()
    {
        const string json = """
            {
              "config": {
                "monthlyLimit": { "val": 2000 },
                "used": { "val": 500 },
                "billingPeriodEnd": "2026-05-01T00:00:00Z"
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = GrokBillingParser.Parse(doc.RootElement, DateTimeOffset.UtcNow);
        Assert.Equal(75, snapshot.Windows[0].RemainingPercent);
    }

    [Fact]
    public void Parses_build_and_bot_product_usage_as_weekly_windows()
    {
        const string json = """
            {
              "config": {
                "currentPeriod": {
                  "type": 2,
                  "end": "2026-06-08T00:00:00Z"
                },
                "productUsage": [
                  { "product": "PRODUCT_GROK_BUILD", "usagePercent": "invalid" },
                  { "product": 4, "usagePercent": 60 },
                  { "product": "PRODUCT_GROK_BUILD", "usagePercent": 25 },
                  { "product": "PRODUCT_API", "usagePercent": 99 }
                ]
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var snapshot = GrokBillingParser.Parse(doc.RootElement, DateTimeOffset.UtcNow);

        Assert.Collection(
            snapshot.Windows,
            build =>
            {
                Assert.Equal("Build", build.Label);
                Assert.Equal(10080, build.DurationMinutes);
                Assert.Equal(75, build.RemainingPercent);
            },
            bot =>
            {
                Assert.Equal("Bot", bot.Label);
                Assert.Equal(10080, bot.DurationMinutes);
                Assert.Equal(40, bot.RemainingPercent);
            });
    }

    [Fact]
    public void Unsupported_nonempty_product_usage_does_not_use_aggregate_fallback()
    {
        const string json = """
            {
              "config": {
                "creditUsagePercent": 20,
                "productUsage": [
                  { "product": "PRODUCT_API", "usagePercent": 20 }
                ]
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var snapshot = GrokBillingParser.Parse(doc.RootElement, DateTimeOffset.UtcNow);

        Assert.Empty(snapshot.Windows);
    }

}