using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Providers;
using BuildnBits.Usage.Core.Providers.Agy;
using BuildnBits.Usage.Core.Refresh;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Tests;

public sealed class RefreshProgressTests
{
    [Fact]
    public async Task Publishes_fast_provider_before_slow_provider_finishes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-progress-" + Guid.NewGuid());
        var codex = new ControlledProvider();
        var grok = new ImmediateProvider(ProviderKind.Grok);
        var published = new TaskCompletionSource<CombinedUsageState>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var service = new UsageRefreshService(
            codex,
            grok,
            cache: new UsageCache(dir),
            interval: TimeSpan.FromHours(1));
        service.StateChanged += (_, state) =>
        {
            if (state.Grok.Status == UsageStatus.Ok)
            {
                published.TrySetResult(state);
            }
        };

        var refresh = service.RefreshNowAsync();
        var completed = await Task.WhenAny(published.Task, Task.Delay(TimeSpan.FromSeconds(2)));

        Assert.Same(published.Task, completed);
        var publishedState = await published.Task;
        Assert.Equal(UsageStatus.Unknown, publishedState.Codex.Status);
        Assert.False(refresh.IsCompleted);

        codex.Complete(Ok(ProviderKind.Codex));
        await refresh;
        Assert.Equal(UsageStatus.Ok, service.Current.Codex.Status);
    }

    [Fact]
    public async Task Concurrent_refresh_requests_share_one_provider_pass()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-coalesce-" + Guid.NewGuid());
        var codex = new ControlledProvider();
        var grok = new ControlledProvider();
        using var service = new UsageRefreshService(
            codex,
            grok,
            cache: new UsageCache(dir),
            interval: TimeSpan.FromHours(1));

        var first = service.RefreshNowAsync();
        await WaitUntilAsync(() => codex.Calls == 1 && grok.Calls == 1);
        var second = service.RefreshNowAsync();

        Assert.Same(first, second);
        codex.Complete(Ok(ProviderKind.Codex));
        grok.Complete(Ok(ProviderKind.Grok));
        await Task.WhenAll(first, second);
        Assert.Equal(1, codex.Calls);
        Assert.Equal(1, grok.Calls);
    }

    [Fact]
    public async Task Subscriber_failure_does_not_stop_refresh_completion()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-subscriber-" + Guid.NewGuid());
        var codex = new ImmediateProvider(ProviderKind.Codex);
        var grok = new ImmediateProvider(ProviderKind.Grok);
        using var service = new UsageRefreshService(
            codex,
            grok,
            cache: new UsageCache(dir),
            interval: TimeSpan.FromHours(1));
        service.StateChanged += (_, _) => throw new InvalidOperationException("test subscriber failure");

        await service.RefreshNowAsync();
        await service.RefreshNowAsync();

        Assert.Equal(2, codex.Calls);
        Assert.Equal(2, grok.Calls);
        Assert.Equal(UsageStatus.Ok, service.Current.Grok.Status);
    }

    [Fact]
    public async Task Agy_timeout_is_bounded_and_reported_as_stale_error()
    {
        var dir = Path.Combine(Path.GetTempPath(), "bnb-agy-timeout-" + Guid.NewGuid());
        Directory.CreateDirectory(dir);
        var executable = Path.Combine(dir, "agy-timeout-test.exe");
        await File.WriteAllBytesAsync(executable, [0]);
        var client = new AgyUsageClient(
            executableName: executable,
            commandRunner: async (_, _, cancellationToken) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return new AgyCommandResult(0, "", "");
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var snapshot = await client.FetchAsync(CancellationToken.None);
        Assert.Equal(UsageStatus.Error, snapshot.Status);
        Assert.Contains("timed out", snapshot.StatusMessage, StringComparison.OrdinalIgnoreCase);
    }

    private static ProviderSnapshot Ok(ProviderKind kind) => new(
        kind,
        UsageStatus.Ok,
        "test",
        [new UsageWindow("Weekly", null, 10, 90, DateTimeOffset.UtcNow.AddDays(1))],
        DateTimeOffset.UtcNow,
        null);

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(2);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    private sealed class ImmediateProvider(ProviderKind kind) : IUsageProvider
    {
        public int Calls { get; private set; }

        public Task<ProviderSnapshot> FetchAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Ok(kind));
        }
    }

    private sealed class ControlledProvider : IUsageProvider
    {
        private readonly TaskCompletionSource<ProviderSnapshot> _result =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int Calls { get; private set; }

        public Task<ProviderSnapshot> FetchAsync(CancellationToken cancellationToken)
        {
            Calls++;
            return _result.Task.WaitAsync(cancellationToken);
        }

        public void Complete(ProviderSnapshot snapshot) => _result.TrySetResult(snapshot);
    }
}
