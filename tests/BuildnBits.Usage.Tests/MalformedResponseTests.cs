using System.Text.Json;
using BuildnBits.Usage.Core.Providers.Codex;
using BuildnBits.Usage.Core.Providers.Grok;

namespace BuildnBits.Usage.Tests;

public class MalformedResponseTests
{
    [Fact]
    public void Codex_rejects_empty_payload()
    {
        using var doc = JsonDocument.Parse("{}");
        Assert.Throws<InvalidOperationException>(() => CodexRateLimitsParser.Parse(doc.RootElement, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Grok_rejects_payload_without_usage()
    {
        using var doc = JsonDocument.Parse("""{ "config": { "prepaidBalance": { "val": 1 } } }""");
        Assert.Throws<InvalidOperationException>(() => GrokBillingParser.Parse(doc.RootElement, DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Codex_api_key_authMode_on_rate_limits_is_rejected()
    {
        using var doc = JsonDocument.Parse("""
            { "authMode": "apikey", "rateLimits": { "primary": { "usedPercent": 1, "windowDurationMins": 300, "resetsAt": 1 } } }
            """);
        Assert.Throws<CodexApiKeyRejectedException>(() => CodexRateLimitsParser.Parse(doc.RootElement, DateTimeOffset.UtcNow));
    }
}
