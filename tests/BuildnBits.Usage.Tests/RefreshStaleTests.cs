using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Providers;
using BuildnBits.Usage.Core.Refresh;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Tests;

public class RefreshStaleTests
{
    [Fact]
    public async Task Preserves_last_success_on_transient_error()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-refresh-" + Guid.NewGuid());
        var cache = new UsageCache(dir);
        var ok = new UsageWindow("5-hour", 300, 10, 90, DateTimeOffset.UtcNow.AddHours(4));
        var codex = new ScriptedProvider([
            new ProviderSnapshot(ProviderKind.Codex, UsageStatus.Ok, "plus", [ok], DateTimeOffset.UtcNow, null),
            new ProviderSnapshot(ProviderKind.Codex, UsageStatus.Error, null, [], DateTimeOffset.UtcNow, "network failed")
        ]);
        var grok = new ScriptedProvider([
            new ProviderSnapshot(ProviderKind.Grok, UsageStatus.Ok, "SuperGrok",
                [new UsageWindow("Weekly", null, 20, 80, DateTimeOffset.UtcNow.AddDays(3))], DateTimeOffset.UtcNow, null),
            new ProviderSnapshot(ProviderKind.Grok, UsageStatus.Error, null, [], DateTimeOffset.UtcNow, "429")
        ]);

        using var service = new UsageRefreshService(codex, grok, cache, TimeSpan.FromHours(1));
        await service.RefreshNowAsync();
        Assert.Equal(UsageStatus.Ok, service.Current.Codex.Status);
        Assert.Equal(90, service.Current.Codex.Windows[0].RemainingPercent);

        await service.RefreshNowAsync();
        Assert.Equal(UsageStatus.Stale, service.Current.Codex.Status);
        Assert.Equal(90, service.Current.Codex.Windows[0].RemainingPercent);
        Assert.Equal(UsageStatus.Stale, service.Current.Grok.Status);
        Assert.Contains("429", service.Current.Grok.StatusMessage);
    }

    [Fact]
    public async Task Network_exception_keeps_cached_windows()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-refresh-" + Guid.NewGuid());
        var cache = new UsageCache(dir);
        var ok = new ProviderSnapshot(ProviderKind.Codex, UsageStatus.Ok, "plus",
            [new UsageWindow("7-day", 10080, 40, 60, DateTimeOffset.UtcNow.AddDays(2))], DateTimeOffset.UtcNow, null);
        var codex = new ScriptedProvider([ok]);
        var grok = new ScriptedProvider([
            new ProviderSnapshot(ProviderKind.Grok, UsageStatus.Ok, null,
                [new UsageWindow("Weekly", null, 1, 99, null)], DateTimeOffset.UtcNow, null)
        ]);
        using var service = new UsageRefreshService(codex, grok, cache, TimeSpan.FromHours(1));
        await service.RefreshNowAsync();
        codex.ThrowNext = true;
        await service.RefreshNowAsync();
        Assert.Equal(UsageStatus.Stale, service.Current.Codex.Status);
        Assert.Equal(60, service.Current.Codex.Windows[0].RemainingPercent);
    }

    private sealed class ScriptedProvider : IUsageProvider
    {
        private readonly Queue<ProviderSnapshot> _queue;
        private ProviderSnapshot? _last;
        public bool ThrowNext;

        public ScriptedProvider(IEnumerable<ProviderSnapshot> snapshots)
        {
            _queue = new Queue<ProviderSnapshot>(snapshots);
        }

        public Task<ProviderSnapshot> FetchAsync(CancellationToken cancellationToken)
        {
            if (ThrowNext)
            {
                throw new HttpRequestException("network failed");
            }

            if (_queue.Count > 0)
            {
                _last = _queue.Dequeue();
            }

            return Task.FromResult(_last ?? CombinedUsageState.Empty.Codex);
        }
    }
}
