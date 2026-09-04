using System.Text.Json;
using BuildnBits.Usage.Core.Models;

namespace BuildnBits.Usage.Core.Storage;

public sealed class UsageCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly string _path;
    private readonly object _sync = new();

    public UsageCache(string? root = null)
    {
        var dir = StoragePaths.ResolveRoot(root);
        _path = Path.Combine(dir, "usage-cache.json");
    }

    public string PathOnDisk => _path;

    public CombinedUsageState Load()
    {
        lock (_sync)
        {
            if (!File.Exists(_path))
            {
                return CombinedUsageState.Empty;
            }

            try
            {
                var json = File.ReadAllText(_path);
                var doc = JsonSerializer.Deserialize<CachedUsageDocument>(json, JsonOptions);
                if (doc is null)
                {
                    return CombinedUsageState.Empty;
                }

                return new CombinedUsageState(
                    FromCache(ProviderKind.Codex, doc.Codex),
                    FromCache(ProviderKind.Grok, doc.Grok),
                    FromCache(ProviderKind.Agy, doc.Agy),
                    doc.LastSuccessfulRefreshUtc,
                    doc.LastAttemptUtc);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                return CombinedUsageState.Empty;
            }
        }
    }

    public void Save(CombinedUsageState state)
    {
        var doc = new CachedUsageDocument
        {
            Codex = ToCache(state.Codex),
            Grok = ToCache(state.Grok),
            Agy = ToCache(state.Agy),
            LastSuccessfulRefreshUtc = state.LastSuccessfulRefreshUtc,
            LastAttemptUtc = state.LastAttemptUtc
        };

        lock (_sync)
        {
            var tmp = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                File.WriteAllText(tmp, JsonSerializer.Serialize(doc, JsonOptions));
                File.Move(tmp, _path, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tmp))
                    {
                        File.Delete(tmp);
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // Preserve the original save exception, if any.
                }
            }
        }
    }

    private static ProviderCacheEntry ToCache(ProviderSnapshot snapshot) => new()
    {
        PlanLabel = snapshot.PlanLabel,
        Status = snapshot.Status.ToString(),
        StatusMessage = snapshot.StatusMessage,
        FetchedAtUtc = snapshot.FetchedAtUtc,
        Windows = snapshot.Windows.Select(w => new CachedWindow
        {
            Label = w.Label,
            DurationMinutes = w.DurationMinutes,
            RemainingPercent = w.RemainingPercent,
            UsedPercent = w.UsedPercent,
            ResetsAtUtc = w.ResetsAtUtc
        }).ToList()
    };

    private static ProviderSnapshot FromCache(ProviderKind kind, ProviderCacheEntry? entry)
    {
        if (entry is null)
        {
            return new ProviderSnapshot(kind, UsageStatus.Unknown, null, [], null, null);
        }

        Enum.TryParse<UsageStatus>(entry.Status, out var status);
        var cachedWindows = entry.Windows ?? [];
        var windows = cachedWindows.Select(w =>
            new UsageWindow(w.Label, w.DurationMinutes, w.UsedPercent, w.RemainingPercent, w.ResetsAtUtc)).ToList();
        var effective = status == UsageStatus.Ok ? UsageStatus.Stale : status;
        return new ProviderSnapshot(kind, effective, entry.PlanLabel, windows, entry.FetchedAtUtc, entry.StatusMessage);
    }
}
