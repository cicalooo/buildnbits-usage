using BuildnBits.Usage.Core.Models;

namespace BuildnBits.Usage.Core.Refresh;

/// <summary>
/// Immutable progress for the current provider refresh operation.
/// </summary>
public sealed record RefreshProgress(
    bool IsRefreshing,
    DateTimeOffset? StartedAtUtc,
    IReadOnlyList<ProviderKind> PendingProviders)
{
    public static RefreshProgress Idle { get; } = new(false, null, []);
}
