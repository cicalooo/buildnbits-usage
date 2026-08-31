using BuildnBits.Usage.Core.Models;
using BuildnBits.Usage.Core.Providers;
using BuildnBits.Usage.Core.Providers.Codex;
using BuildnBits.Usage.Core.Providers.Grok;
using BuildnBits.Usage.Core.Storage;

namespace BuildnBits.Usage.Core.Refresh;

public sealed class UsageRefreshService : IDisposable
{
    private readonly IUsageProvider _codex;
    private readonly IUsageProvider _grok;
    private readonly UsageCache _cache;
    private readonly TimeSpan _interval;
    private readonly Random _jitter = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CombinedUsageState _state = CombinedUsageState.Empty;
    private Task? _loop;

    public event EventHandler<CombinedUsageState>? StateChanged;

    public CombinedUsageState Current => _state;

    public UsageRefreshService(
        IUsageProvider? codex = null,
        IUsageProvider? grok = null,
        UsageCache? cache = null,
        TimeSpan? interval = null)
    {
        _codex = codex ?? new CodexUsageClient();
        _grok = grok ?? new GrokUsageClient();
        _cache = cache ?? new UsageCache();
        _interval = interval ?? TimeSpan.FromMinutes(10);
        _state = _cache.Load();
    }

    public void Start()
    {
        _loop ??= Task.Run(() => LoopAsync(_cts.Token));
    }

    public Task RefreshNowAsync(CancellationToken cancellationToken = default) =>
        RefreshCoreAsync(cancellationToken);

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        while (!cancellationToken.IsCancellationRequested)
        {
            var jitterSeconds = _jitter.Next(-45, 46);
            var delay = _interval + TimeSpan.FromSeconds(jitterSeconds);
            try
            {
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var previous = _state;
            ProviderSnapshot codex;
            ProviderSnapshot grok;
            try
            {
                var codexTask = _codex.FetchAsync(cancellationToken);
                var grokTask = _grok.FetchAsync(cancellationToken);
                await Task.WhenAll(codexTask, grokTask).ConfigureAwait(false);
                codex = await codexTask.ConfigureAwait(false);
                grok = await grokTask.ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                _state = PreserveOnFailure(previous, ex.Message);
                StateChanged?.Invoke(this, _state);
                return;
            }

            var now = DateTimeOffset.UtcNow;
            codex = Merge(previous.Codex, codex);
            grok = Merge(previous.Grok, grok);
            var success = IsSuccess(codex) || IsSuccess(grok);
            _state = new CombinedUsageState(
                codex,
                grok,
                success ? now : previous.LastSuccessfulRefreshUtc,
                now);
            if (success)
            {
                _cache.Save(_state);
            }

            StateChanged?.Invoke(this, _state);
        }
        finally
        {
            _gate.Release();
        }
    }

    private static bool IsSuccess(ProviderSnapshot snapshot) =>
        snapshot.Status is UsageStatus.Ok or UsageStatus.AuthRejected or UsageStatus.Unauthenticated or UsageStatus.MissingCli;

    private static ProviderSnapshot Merge(ProviderSnapshot previous, ProviderSnapshot next)
    {
        if (next.Status is UsageStatus.Ok or UsageStatus.AuthRejected or UsageStatus.Unauthenticated or UsageStatus.MissingCli)
        {
            return next;
        }

        if (previous.Windows.Count == 0)
        {
            return next;
        }

        return previous with
        {
            Status = UsageStatus.Stale,
            StatusMessage = next.StatusMessage ?? previous.StatusMessage
        };
    }

    private static CombinedUsageState PreserveOnFailure(CombinedUsageState previous, string message) =>
        previous with
        {
            Codex = previous.Codex with { Status = previous.Codex.Windows.Count > 0 ? UsageStatus.Stale : UsageStatus.Error, StatusMessage = message },
            Grok = previous.Grok with { Status = previous.Grok.Windows.Count > 0 ? UsageStatus.Stale : UsageStatus.Error, StatusMessage = message },
            LastAttemptUtc = DateTimeOffset.UtcNow
        };

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _gate.Dispose();
    }
}
