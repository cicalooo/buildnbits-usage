using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Refresh;
using BuildnBits.Usage.Core.Storage;
using BuildnBits.Usage.Core.Widgets;

namespace BuildnBits.Usage.Tests;

public class CacheAndRefreshTests
{
    [Fact]
    public void Cache_roundtrips_percentages_not_secrets()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-usage-" + Guid.NewGuid());
        var cache = new UsageCache(dir);
        var state = new CombinedUsageState(
            new ProviderSnapshot(ProviderKind.Codex, UsageStatus.Ok, "plus",
                [new UsageWindow("5-hour", 300, 20, 80, DateTimeOffset.UtcNow.AddHours(3))],
                DateTimeOffset.UtcNow, null),
            new ProviderSnapshot(ProviderKind.Grok, UsageStatus.Ok, "SuperGrok",
                [new UsageWindow("Weekly", null, 40, 60, DateTimeOffset.UtcNow.AddDays(4))],
                DateTimeOffset.UtcNow, null),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow)
        {
            Agy = new ProviderSnapshot(ProviderKind.Agy, UsageStatus.Ok, "Pro",
                [new UsageWindow("Gemini Models · Weekly Limit Remaining", 10080, 5, 95, DateTimeOffset.UtcNow.AddDays(6))],
                DateTimeOffset.UtcNow, null)
        };
        cache.Save(state);
        var json = File.ReadAllText(cache.PathOnDisk);
        Assert.DoesNotContain("token", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("cookie", json, StringComparison.OrdinalIgnoreCase);
        var loaded = cache.Load();
        Assert.Equal(80, loaded.Codex.Windows[0].RemainingPercent);
        Assert.Equal(UsageStatus.Stale, loaded.Codex.Status);
        Assert.Equal(95, loaded.Agy.Windows[0].RemainingPercent);
        Assert.Equal(UsageStatus.Stale, loaded.Agy.Status);
    }

    [Fact]
    public void Adaptive_card_contains_refresh_and_open_actions()
    {
        var template = UsageAdaptiveCard.TemplateJson();
        Assert.Contains("\"verb\": \"refresh\"", template);
        Assert.Contains("\"verb\": \"openApp\"", template);
        Assert.Contains("small", template);
        var data = UsageAdaptiveCard.DataJson(CombinedUsageState.Empty, DateTimeOffset.UtcNow);
        Assert.Contains("codexFiveRemaining", data);
        Assert.Contains("agyRemaining", data);
    }

    [Fact]
    public async Task Missing_cli_is_reported_without_throwing()
    {
        var client = new BuildnBits.Usage.Core.Providers.Codex.CodexUsageClient("codex-definitely-missing.exe");
        var snapshot = await client.FetchAsync(CancellationToken.None);
        Assert.Equal(UsageStatus.MissingCli, snapshot.Status);
    }

    [Fact]
    public async Task Grok_missing_cli_is_reported()
    {
        var client = new BuildnBits.Usage.Core.Providers.Grok.GrokUsageClient("grok-definitely-missing.exe");
        var snapshot = await client.FetchAsync(CancellationToken.None);
        Assert.Equal(UsageStatus.MissingCli, snapshot.Status);
    }
}
