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
    private readonly IUsageProvider? _agy;
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
        TimeSpan? interval = null,
        IUsageProvider? agy = null)
    {
        _codex = codex ?? new CodexUsageClient();
        _grok = grok ?? new GrokUsageClient();
        _agy = agy;
        _cache = cache ?? new UsageCache();
        _interval = interval ?? TimeSpan.FromMinutes(10);
        _state = _cache.Load();
    }

    public UsageRefreshService(
        IUsageProvider codex,
        IUsageProvider grok,
        IUsageProvider agy,
        UsageCache? cache = null,
        TimeSpan? interval = null)
        : this(codex, grok, cache, interval, agy)
    {
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
            var codexTask = FetchSafelyAsync(_codex, ProviderKind.Codex, cancellationToken);
            var grokTask = FetchSafelyAsync(_grok, ProviderKind.Grok, cancellationToken);
            var agyTask = _agy is null
                ? null
                : FetchSafelyAsync(_agy, ProviderKind.Agy, cancellationToken);

            if (agyTask is null)
            {
                await Task.WhenAll(codexTask, grokTask).ConfigureAwait(false);
            }
            else
            {
                await Task.WhenAll(codexTask, grokTask, agyTask).ConfigureAwait(false);
            }

            var now = DateTimeOffset.UtcNow;
            var codex = Merge(previous.Codex, await codexTask.ConfigureAwait(false));
            var grok = Merge(previous.Grok, await grokTask.ConfigureAwait(false));
            var agy = agyTask is null
                ? previous.Agy
                : Merge(previous.Agy, await agyTask.ConfigureAwait(false));
            var success = IsSuccess(codex) || IsSuccess(grok) ||
                          (agyTask is not null && IsSuccess(agy));
            _state = new CombinedUsageState(
                codex,
                grok,
                agy,
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

    private static async Task<ProviderSnapshot> FetchSafelyAsync(
        IUsageProvider provider,
        ProviderKind kind,
        CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await provider.FetchAsync(cancellationToken).ConfigureAwait(false);
            return snapshot.Provider == kind ? snapshot : snapshot with { Provider = kind };
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ProviderSnapshot(
                kind,
                UsageStatus.Error,
                null,
                [],
                DateTimeOffset.UtcNow,
                SanitizeError(ex.Message));
        }
    }

    private static ProviderSnapshot Merge(ProviderSnapshot previous, ProviderSnapshot next)
    {
        if (IsSuccess(next))
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

    private static string SanitizeError(string message)
    {
        if (message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("bearer", StringComparison.OrdinalIgnoreCase))
        {
            return "Provider request failed.";
        }

        return string.IsNullOrWhiteSpace(message) ? "Provider request failed." : message;
    }

    public void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        _gate.Dispose();
    }
}
