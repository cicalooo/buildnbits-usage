using System.Text.Json;
using BuildnBits.Usage.Core.Providers.Grok;

namespace BuildnBits.Usage.Tests;

public class GrokParsingTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-06-02T00:00:00Z");

    [Fact]
    public void Parses_weekly_credits_config_as_build_when_no_product_usage()
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
        var snapshot = GrokBillingParser.Parse(doc.RootElement, Now);
        Assert.Equal("SuperGrok", snapshot.PlanLabel);
        Assert.Single(snapshot.Windows);
        Assert.Equal("Build", snapshot.Windows[0].Label);
        Assert.Equal(58, snapshot.Windows[0].RemainingPercent);
        Assert.Equal(10080, snapshot.Windows[0].DurationMinutes);
        Assert.Equal(new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero), snapshot.Windows[0].ResetsAtUtc);
    }

    [Fact]
    public void Parses_build_and_bot_from_product_usage()
    {
        const string json = """
            {
              "config": {
                "creditUsagePercent": 100,
                "productUsage": [
                  { "product": 2, "usagePercent": 46 },
                  { "product": 7, "usagePercent": 45 },
                  { "product": 4, "usagePercent": 7 },
                  { "product": 8, "usagePercent": 2 }
                ],
                "currentPeriod": {
                  "type": "USAGE_PERIOD_TYPE_WEEKLY",
                  "start": "2026-08-28T13:21:21.177227Z",
                  "end": "2026-09-04T13:21:21.177227Z"
                }
              },
              "subscriptionTier": "SuperGrok"
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = GrokBillingParser.Parse(doc.RootElement, Now);
        Assert.Equal(2, snapshot.Windows.Count);
        Assert.Equal("Build", snapshot.Windows[0].Label);
        Assert.Equal(54, snapshot.Windows[0].RemainingPercent);
        Assert.Equal("Bot", snapshot.Windows[1].Label);
        Assert.Equal(93, snapshot.Windows[1].RemainingPercent);
        Assert.Equal(snapshot.Windows[0].ResetsAtUtc, snapshot.Windows[1].ResetsAtUtc);
        Assert.Equal(54, snapshot.LowestRemainingPercent);
    }

    [Fact]
    public void Parses_string_product_enums()
    {
        const string json = """
            {
              "config": {
                "creditUsagePercent": 10,
                "productUsage": [
                  { "product": "PRODUCT_GROK_BUILD", "usagePercent": 61.2 },
                  { "product": "PRODUCT_GROK_BOT", "usagePercent": 12.4 }
                ],
                "currentPeriod": {
                  "type": "USAGE_PERIOD_TYPE_WEEKLY",
                  "end": "2026-06-08T00:00:00Z"
                }
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = GrokBillingParser.Parse(doc.RootElement, Now);
        Assert.Equal("Build", snapshot.Windows[0].Label);
        Assert.Equal(39, snapshot.Windows[0].RemainingPercent);
        Assert.Equal("Bot", snapshot.Windows[1].Label);
        Assert.Equal(88, snapshot.Windows[1].RemainingPercent);
    }

    [Fact]
    public void Ignores_unknown_products_and_omits_missing_bot()
    {
        const string json = """
            {
              "config": {
                "creditUsagePercent": 20,
                "productUsage": [
                  { "product": "PRODUCT_GROK_BUILD", "usagePercent": 20 },
                  { "product": "PRODUCT_IMAGINE", "usagePercent": 5 }
                ]
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);
        var snapshot = GrokBillingParser.Parse(doc.RootElement, Now);
        Assert.Single(snapshot.Windows);
        Assert.Equal("Build", snapshot.Windows[0].Label);
        Assert.DoesNotContain(snapshot.Windows, w => w.Label == "Bot");
    }

    [Fact]
    public void Falls_back_to_legacy_cents_as_build()
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
        Assert.Equal("Build", snapshot.Windows[0].Label);
        Assert.Equal(75, snapshot.Windows[0].RemainingPercent);
    }
}
