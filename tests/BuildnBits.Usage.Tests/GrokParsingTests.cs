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
    public void Treats_active_period_only_billing_as_zero_used()
    {
        const string json = """
            {
              "config": {
                "currentPeriod": {
                  "type": "USAGE_PERIOD_TYPE_WEEKLY",
                  "start": "2026-06-01T00:00:00Z",
                  "end": "2026-06-08T00:00:00Z"
                },
                "onDemandCap": { "val": 0 },
                "onDemandUsed": { "val": 0 },
                "prepaidBalance": { "val": 0 },
                "isUnifiedBillingUser": true,
                "billingPeriodStart": "2026-06-01T00:00:00Z",
                "billingPeriodEnd": "2026-06-08T00:00:00Z"
              },
              "subscription_tier": "SuperGrok"
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var snapshot = GrokBillingParser.Parse(doc.RootElement, Now);

        var build = Assert.Single(snapshot.Windows);
        Assert.Equal("Build", build.Label);
        Assert.Equal(0, build.UsedPercent);
        Assert.Equal(100, build.RemainingPercent);
        Assert.Equal(10080, build.DurationMinutes);
        Assert.Equal(new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero), build.ResetsAtUtc);
    }

    [Fact]
    public void Does_not_infer_zero_for_inactive_period_only_billing()
    {
        const string json = """
            {
              "config": {
                "currentPeriod": {
                  "type": "USAGE_PERIOD_TYPE_WEEKLY",
                  "start": "2026-05-25T00:00:00Z",
                  "end": "2026-06-01T00:00:00Z"
                }
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);

        Assert.Throws<InvalidOperationException>(() => GrokBillingParser.Parse(doc.RootElement, Now));
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

    [Fact]
    public void Skips_nonfinite_usage_percent_before_first_valid_duplicate()
    {
        const string json = """
            {
              "config": {
                "productUsage": [
                  { "product": "PRODUCT_GROK_BUILD", "usagePercent": 1e999 },
                  { "product": "PRODUCT_GROK_BUILD", "usagePercent": 25 }
                ]
              }
            }
            """;
        using var doc = JsonDocument.Parse(json);

        var snapshot = GrokBillingParser.Parse(doc.RootElement, Now);

        var build = Assert.Single(snapshot.Windows);
        Assert.Equal("Build", build.Label);
        Assert.Equal(75, build.RemainingPercent);
    }

    [Fact]
    public void Rejects_malformed_present_usage_even_with_active_period()
    {
        foreach (var usageFields in new[]
                 {
                     "\"creditUsagePercent\": \"invalid\"",
                     "\"monthlyLimit\": { \"val\": \"invalid\" }, \"used\": { \"val\": 1 }",
                     "\"monthlyLimit\": { \"val\": 2000 }, \"used\": { \"val\": \"invalid\" }"
                 })
        {
            using var doc = JsonDocument.Parse(PeriodOnlyBilling(usageFields: usageFields));

            Assert.Throws<InvalidOperationException>(() => GrokBillingParser.Parse(doc.RootElement, Now));
        }
    }

    [Fact]
    public void Rejects_untrusted_period_only_variants()
    {
        var payloads = new[]
        {
            PeriodOnlyBilling(includeMarkers: false),
            PeriodOnlyBilling(type: "NOT_WEEKLY"),
            PeriodOnlyBilling(start: "2026-06-03T00:00:00Z", end: "2026-06-10T00:00:00Z"),
            PeriodOnlyBilling(billingEnd: "2026-06-09T00:00:00Z")
        };

        foreach (var payload in payloads)
        {
            using var doc = JsonDocument.Parse(payload);

            Assert.Throws<InvalidOperationException>(() => GrokBillingParser.Parse(doc.RootElement, Now));
        }
    }

    [Fact]
    public void Rejects_malformed_unified_billing_markers()
    {
        var payloads = new[]
        {
            PeriodOnlyBilling().Replace(
                "\"onDemandCap\": { \"val\": 0 }",
                "\"onDemandCap\": { \"val\": \"invalid\" }",
                StringComparison.Ordinal),
            PeriodOnlyBilling().Replace(
                "\"onDemandUsed\": { \"val\": 0 }",
                "\"onDemandUsed\": { \"val\": \"1\" }",
                StringComparison.Ordinal)
        };

        foreach (var payload in payloads)
        {
            using var doc = JsonDocument.Parse(payload);

            Assert.Throws<InvalidOperationException>(() => GrokBillingParser.Parse(doc.RootElement, Now));
        }
    }

    private static string PeriodOnlyBilling(
        string type = "USAGE_PERIOD_TYPE_WEEKLY",
        string start = "2026-06-01T00:00:00Z",
        string end = "2026-06-08T00:00:00Z",
        bool includeMarkers = true,
        string? billingStart = null,
        string? billingEnd = null,
        string? usageFields = null)
    {
        var usage = usageFields is null ? string.Empty : $"                {usageFields},\n";
        var markers = includeMarkers
            ? """
                "onDemandCap": { "val": 0 },
                "onDemandUsed": { "val": 0 },
                "prepaidBalance": { "val": 0 },
                "isUnifiedBillingUser": true,
            """
            : string.Empty;

        return $$"""
            {
              "config": {
                "currentPeriod": {
                  "type": "{{type}}",
                  "start": "{{start}}",
                  "end": "{{end}}"
                },
                {{usage}}{{markers}}                "billingPeriodStart": "{{billingStart ?? start}}",
                "billingPeriodEnd": "{{billingEnd ?? end}}"
              }
            }
            """;
    }
}
