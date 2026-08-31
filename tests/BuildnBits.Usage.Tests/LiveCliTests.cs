using BuildnBits.Usage.Core.Providers.Codex;
using BuildnBits.Usage.Core.Providers.Grok;

namespace BuildnBits.Usage.Tests;

public class LiveCliTests
{
    [Fact]
    public async Task Codex_app_server_returns_subscription_windows()
    {
        var snapshot = await new CodexUsageClient().FetchAsync(CancellationToken.None);
        Assert.True(
            snapshot.Status is BuildnBits.Usage.Core.Models.UsageStatus.Ok
                or BuildnBits.Usage.Core.Models.UsageStatus.Unauthenticated
                or BuildnBits.Usage.Core.Models.UsageStatus.AuthRejected
                or BuildnBits.Usage.Core.Models.UsageStatus.MissingCli,
            snapshot.StatusMessage);
        if (snapshot.Status == BuildnBits.Usage.Core.Models.UsageStatus.Ok)
        {
            Assert.Contains(snapshot.Windows, w => w.DurationMinutes == 300);
            Assert.Contains(snapshot.Windows, w => w.DurationMinutes == 10080);
        }
    }

    [Fact]
    public async Task Grok_agent_stdio_does_not_stay_unknown()
    {
        var snapshot = await new GrokUsageClient().FetchAsync(CancellationToken.None);
        Assert.NotEqual(BuildnBits.Usage.Core.Models.UsageStatus.Unknown, snapshot.Status);
        Assert.NotNull(snapshot.FetchedAtUtc);
    }
}
