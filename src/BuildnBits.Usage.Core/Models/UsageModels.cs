namespace BuildnBits.Usage.Core.Models;

public enum ProviderKind
{
    Codex,
    Grok
}

public enum UsageStatus
{
    Unknown,
    Ok,
    Stale,
    Error,
    MissingCli,
    AuthRejected,
    Unauthenticated
}

public sealed record UsageWindow(
    string Label,
    int? DurationMinutes,
    double UsedPercent,
    double RemainingPercent,
    DateTimeOffset? ResetsAtUtc);

public sealed record ProviderSnapshot(
    ProviderKind Provider,
    UsageStatus Status,
    string? PlanLabel,
    IReadOnlyList<UsageWindow> Windows,
    DateTimeOffset? FetchedAtUtc,
    string? StatusMessage)
{
    public double? LowestRemainingPercent =>
        Windows.Count == 0 ? null : Windows.Min(w => w.RemainingPercent);

    public UsageWindow? WindowByDuration(int minutes) =>
        Windows.FirstOrDefault(w => w.DurationMinutes == minutes);

    public UsageWindow? Weekly =>
        Windows.FirstOrDefault(w =>
            string.Equals(w.Label, "Weekly", StringComparison.OrdinalIgnoreCase) ||
            w.DurationMinutes is 10080);
}

public sealed record CombinedUsageState(
    ProviderSnapshot Codex,
    ProviderSnapshot Grok,
    DateTimeOffset? LastSuccessfulRefreshUtc,
    DateTimeOffset? LastAttemptUtc)
{
    public static CombinedUsageState Empty { get; } = new(
        new ProviderSnapshot(ProviderKind.Codex, UsageStatus.Unknown, null, [], null, null),
        new ProviderSnapshot(ProviderKind.Grok, UsageStatus.Unknown, null, [], null, null),
        null,
        null);
}

public sealed class CachedUsageDocument
{
    public ProviderCacheEntry? Codex { get; set; }
    public ProviderCacheEntry? Grok { get; set; }
    public DateTimeOffset? LastSuccessfulRefreshUtc { get; set; }
}

public sealed class ProviderCacheEntry
{
    public string? PlanLabel { get; set; }
    public string Status { get; set; } = nameof(UsageStatus.Unknown);
    public string? StatusMessage { get; set; }
    public DateTimeOffset? FetchedAtUtc { get; set; }
    public List<CachedWindow> Windows { get; set; } = [];
}

public sealed class CachedWindow
{
    public string Label { get; set; } = "";
    public int? DurationMinutes { get; set; }
    public double RemainingPercent { get; set; }
    public double UsedPercent { get; set; }
    public DateTimeOffset? ResetsAtUtc { get; set; }
}
