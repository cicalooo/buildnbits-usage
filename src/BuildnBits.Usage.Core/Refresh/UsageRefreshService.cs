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
    private readonly AppLog _log;
    private readonly bool _ownsLog;
    private readonly CancellationTokenSource _cts = new();
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly SemaphoreSlim _intervalChanged = new(0, 1);
    private readonly object _intervalSync = new();
    private TimeSpan _interval;
    private CombinedUsageState _state = CombinedUsageState.Empty;
    private Task? _loop;
    private int _started;
    private int _disposed;

    public event EventHandler<CombinedUsageState>? StateChanged;

    public CombinedUsageState Current => _state;

    public TimeSpan Interval
    {
        get
        {
            lock (_intervalSync)
            {
                return _interval;
            }
        }
    }

    public UsageRefreshService(
        IUsageProvider? codex = null,
        IUsageProvider? grok = null,
        UsageCache? cache = null,
        TimeSpan? interval = null,
        IUsageProvider? agy = null,
        AppLog? appLog = null)
    {
        _codex = codex ?? new CodexUsageClient();
        _grok = grok ?? new GrokUsageClient();
        _agy = agy;
        _cache = cache ?? new UsageCache();
        _interval = NormalizeInterval(interval ?? TimeSpan.FromMinutes(AppSettings.DefaultRefreshIntervalMinutes));
        _log = appLog ?? new AppLog();
        _ownsLog = appLog is null;
        _state = _cache.Load();
        _log.Info($"Refresh service startup; interval={FormatInterval(_interval)}.");
    }

    public UsageRefreshService(
        IUsageProvider codex,
        IUsageProvider grok,
        IUsageProvider agy,
        UsageCache? cache = null,
        TimeSpan? interval = null,
        AppLog? appLog = null)
        : this(codex, grok, cache, interval, agy, appLog)
    {
    }

    public void UpdateInterval(TimeSpan interval)
    {
        var next = NormalizeInterval(interval);
        var changed = false;
        lock (_intervalSync)
        {
            if (_interval != next)
            {
                _interval = next;
                changed = true;
            }
        }

        if (!changed)
        {
            return;
        }

        _log.Info($"Refresh interval changed to {FormatInterval(next)}.");
        if (Volatile.Read(ref _started) != 0)
        {
            try
            {
                _intervalChanged.Release();
            }
            catch (SemaphoreFullException)
            {
                // A refresh loop will observe the already queued change.
            }
            catch (ObjectDisposedException)
            {
                // The service is shutting down.
            }
        }
    }

    public void SetInterval(TimeSpan interval) => UpdateInterval(interval);

    public void Start()
    {
        if (Volatile.Read(ref _disposed) != 0 ||
            Interlocked.CompareExchange(ref _started, 1, 0) != 0)
        {
            return;
        }

        _loop = Task.Run(() => LoopAsync(_cts.Token));
    }

    public async Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);
        await RefreshCoreAsync(linked.Token).ConfigureAwait(false);
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            while (!cancellationToken.IsCancellationRequested)
            {
                var interval = Interval;
                var jitterSeconds = GetJitterSeconds(interval);
                var delaySeconds = Math.Max(0.05, interval.TotalSeconds + jitterSeconds);

                using var waitCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                var delayTask = Task.Delay(TimeSpan.FromSeconds(delaySeconds), waitCts.Token);
                var changeTask = _intervalChanged.WaitAsync(waitCts.Token);
                var completed = await Task.WhenAny(delayTask, changeTask).ConfigureAwait(false);

                if (cancellationToken.IsCancellationRequested)
                {
                    break;
                }

                if (completed == changeTask)
                {
                    waitCts.Cancel();
                    try
                    {
                        await delayTask.ConfigureAwait(false);
                    }
                    catch (OperationCanceledException)
                    {
                        // The interval changed; recalculate the delay below.
                    }

                    continue;
                }

                waitCts.Cancel();
                try
                {
                    await changeTask.ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    // The delay elapsed; refresh now.
                }

                await RefreshCoreAsync(cancellationToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _log.Error($"Refresh loop stopped: {ex.Message}");
        }
    }

    private async Task RefreshCoreAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        _log.Info("Refresh start.");
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

            LogProvider(codex);
            LogProvider(grok);
            if (agyTask is not null)
            {
                LogProvider(agy);
            }

            StateChanged?.Invoke(this, _state);
            _log.Info($"Refresh end; success={success}.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _log.Info("Refresh canceled.");
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Refresh failed: {ex.Message}");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    private void LogProvider(ProviderSnapshot snapshot)
    {
        var detail = string.IsNullOrWhiteSpace(snapshot.StatusMessage)
            ? string.Empty
            : $" message={snapshot.StatusMessage}";
        _log.Info(
            $"Provider {snapshot.Provider}: status={snapshot.Status}, windows={snapshot.Windows.Count}, " +
            $"plan={snapshot.PlanLabel ?? "unknown"}.{detail}");
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
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Provider request failed.";
        }

        if (message.Contains("token", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("cookie", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("authorization", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("bearer", StringComparison.OrdinalIgnoreCase) ||
            message.Contains("password", StringComparison.OrdinalIgnoreCase))
        {
            return "Provider request failed.";
        }

        return message.Length <= 500 ? message : message[..497] + "...";
    }

    private static TimeSpan NormalizeInterval(TimeSpan interval) =>
        interval <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : interval;

    private static double GetJitterSeconds(TimeSpan interval)
    {
        var max = Math.Clamp(interval.TotalSeconds / 9, 0, 45);
        return (Random.Shared.NextDouble() * 2 - 1) * max;
    }

    private static string FormatInterval(TimeSpan interval) =>
        interval.TotalMinutes >= 1
            ? $"{interval.TotalMinutes:0.##} minutes"
            : $"{interval.TotalSeconds:0.##} seconds";

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
        {
            return;
        }

        _cts.Cancel();
        try
        {
            _intervalChanged.Release();
        }
        catch (SemaphoreFullException)
        {
            // The loop is already awake.
        }

        try
        {
            _loop?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _log.Error($"Refresh shutdown failed: {ex.Message}");
        }

        DisposeProvider(_codex);
        DisposeProvider(_grok);
        DisposeProvider(_agy);
        _intervalChanged.Dispose();
        _gate.Dispose();
        _cts.Dispose();
        if (_ownsLog)
        {
            _log.Dispose();
        }
    }

    private static void DisposeProvider(IUsageProvider? provider)
    {
        if (provider is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
