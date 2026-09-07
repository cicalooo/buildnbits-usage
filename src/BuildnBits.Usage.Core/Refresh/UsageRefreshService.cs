using System.Diagnostics;
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
    private readonly SemaphoreSlim _intervalChanged = new(0, 1);
    private readonly object _intervalSync = new();
    private readonly object _refreshSync = new();
    private TimeSpan _interval;
    private CombinedUsageState _state = CombinedUsageState.Empty;
    private RefreshProgress _progress = RefreshProgress.Idle;
    private Task? _loop;
    private Task? _refreshInFlight;
    private int _started;
    private int _disposed;

    public event EventHandler<CombinedUsageState>? StateChanged;
    public event EventHandler<RefreshProgress>? ProgressChanged;

    public CombinedUsageState Current => Volatile.Read(ref _state);
    public RefreshProgress Progress => Volatile.Read(ref _progress);
    public bool IsRefreshing => Progress.IsRefreshing;

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

    /// <summary>
    /// Requests a refresh. Calls made while a refresh is running await that
    /// same operation instead of starting a second provider pass.
    /// </summary>
    public Task RefreshNowAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);

        Task operation;
        lock (_refreshSync)
        {
            if (_refreshInFlight is null || _refreshInFlight.IsCompleted)
            {
                // Keep provider work off the caller (normally WinForms) thread
                // and install the shared task before releasing the lock.
                _refreshInFlight = Task.Run(() => RefreshCoreAsync(_cts.Token));
            }

            operation = _refreshInFlight;
        }

        return cancellationToken.CanBeCanceled
            ? operation.WaitAsync(cancellationToken)
            : operation;
    }

    private async Task LoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
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

                await RefreshNowAsync(cancellationToken).ConfigureAwait(false);
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
        var started = DateTimeOffset.UtcNow;
        var pending = Providers();
        SetProgress(new RefreshProgress(true, started, pending));
        _log.Info($"Refresh start; providers={string.Join(',', pending)}.");

        var tasks = new Dictionary<Task<ProviderSnapshot>, ProviderKind>
        {
            [FetchSafelyAsync(_codex, ProviderKind.Codex, cancellationToken)] = ProviderKind.Codex,
            [FetchSafelyAsync(_grok, ProviderKind.Grok, cancellationToken)] = ProviderKind.Grok
        };
        if (_agy is not null)
        {
            tasks[FetchSafelyAsync(_agy, ProviderKind.Agy, cancellationToken)] = ProviderKind.Agy;
        }

        var anySuccess = false;
        try
        {
            while (tasks.Count > 0)
            {
                var completed = await Task.WhenAny(tasks.Keys).ConfigureAwait(false);
                var kind = tasks[completed];
                tasks.Remove(completed);
                var snapshot = await completed.ConfigureAwait(false);
                anySuccess |= IsSuccess(snapshot);

                var now = DateTimeOffset.UtcNow;
                var current = Current;
                var merged = kind switch
                {
                    ProviderKind.Codex => new CombinedUsageState(
                        Merge(current.Codex, snapshot),
                        current.Grok,
                        current.Agy,
                        IsSuccess(snapshot) ? now : current.LastSuccessfulRefreshUtc,
                        started),
                    ProviderKind.Grok => new CombinedUsageState(
                        current.Codex,
                        Merge(current.Grok, snapshot),
                        current.Agy,
                        IsSuccess(snapshot) ? now : current.LastSuccessfulRefreshUtc,
                        started),
                    _ => new CombinedUsageState(
                        current.Codex,
                        current.Grok,
                        Merge(current.Agy, snapshot),
                        IsSuccess(snapshot) ? now : current.LastSuccessfulRefreshUtc,
                        started)
                };

                PublishState(merged);
                pending.Remove(kind);
                SetProgress(new RefreshProgress(true, started, pending));
            }

            var final = Current;
            if (anySuccess)
            {
                try
                {
                    _cache.Save(final);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
                {
                    _log.Error($"Usage cache save failed: {AppLog.Sanitize(ex.Message)}");
                }
            }

            _log.Info($"Refresh end; success={anySuccess}; duration={(DateTimeOffset.UtcNow - started).TotalSeconds:0.###}s.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _log.Info("Refresh canceled.");
            throw;
        }
        catch (Exception ex)
        {
            _log.Error($"Refresh failed: {AppLog.Sanitize(ex.Message)}");
            throw;
        }
        finally
        {
            SetProgress(RefreshProgress.Idle);
        }
    }

    private List<ProviderKind> Providers()
    {
        var providers = new List<ProviderKind> { ProviderKind.Codex, ProviderKind.Grok };
        if (_agy is not null)
        {
            providers.Add(ProviderKind.Agy);
        }

        return providers;
    }

    private void PublishState(CombinedUsageState state)
    {
        Volatile.Write(ref _state, state);
        var handlers = StateChanged?.GetInvocationList();
        if (handlers is null)
        {
            return;
        }

        foreach (var callback in handlers.OfType<EventHandler<CombinedUsageState>>())
        {
            try
            {
                callback(this, state);
            }
            catch (Exception ex)
            {
                _log.Error($"State subscriber failed: {AppLog.Sanitize(ex.Message)}");
            }
        }
    }

    private void SetProgress(RefreshProgress progress)
    {
        var snapshot = progress.PendingProviders.Count == 0
            ? progress with { PendingProviders = [] }
            : progress with { PendingProviders = Array.AsReadOnly(progress.PendingProviders.ToArray()) };
        Volatile.Write(ref _progress, snapshot);
        var handlers = ProgressChanged?.GetInvocationList();
        if (handlers is null)
        {
            return;
        }

        foreach (var callback in handlers.OfType<EventHandler<RefreshProgress>>())
        {
            try
            {
                callback(this, snapshot);
            }
            catch (Exception ex)
            {
                _log.Error($"Progress subscriber failed: {AppLog.Sanitize(ex.Message)}");
            }
        }
    }

    private void LogProvider(ProviderSnapshot snapshot, TimeSpan duration)
    {
        var detail = string.IsNullOrWhiteSpace(snapshot.StatusMessage)
            ? string.Empty
            : $" message={snapshot.StatusMessage}";
        _log.Info(
            $"Provider {snapshot.Provider}: status={snapshot.Status}, windows={snapshot.Windows.Count}, " +
            $"plan={snapshot.PlanLabel ?? "unknown"}, duration={duration.TotalSeconds:0.###}s.{detail}");
    }

    private static bool IsSuccess(ProviderSnapshot snapshot) =>
        snapshot.Status is UsageStatus.Ok or UsageStatus.AuthRejected or UsageStatus.Unauthenticated or UsageStatus.MissingCli;

    private async Task<ProviderSnapshot> FetchSafelyAsync(
        IUsageProvider provider,
        ProviderKind kind,
        CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var snapshot = await provider.FetchAsync(cancellationToken).ConfigureAwait(false);
            var normalized = snapshot.Provider == kind ? snapshot : snapshot with { Provider = kind };
            LogProvider(normalized, stopwatch.Elapsed);
            return normalized;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            var snapshot = new ProviderSnapshot(
                kind,
                UsageStatus.Error,
                null,
                [],
                DateTimeOffset.UtcNow,
                SanitizeError(ex.Message));
            LogProvider(snapshot, stopwatch.Elapsed);
            return snapshot;
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

        Task? refresh;
        lock (_refreshSync)
        {
            refresh = _refreshInFlight;
        }

        try
        {
            _loop?.GetAwaiter().GetResult();
            refresh?.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _log.Error($"Refresh shutdown failed: {AppLog.Sanitize(ex.Message)}");
        }

        DisposeProvider(_codex);
        DisposeProvider(_grok);
        DisposeProvider(_agy);
        _intervalChanged.Dispose();
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
