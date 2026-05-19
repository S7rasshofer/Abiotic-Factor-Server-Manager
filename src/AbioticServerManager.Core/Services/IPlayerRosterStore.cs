using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Core.Services;

/// <summary>
/// Persists the durable per-world player roster. Like <see cref="IInstanceStore"/>,
/// a missing or corrupt file yields an empty list rather than throwing.
/// </summary>
public interface IPlayerRosterStore
{
    Task<IReadOnlyList<PlayerRosterEntry>> LoadAsync(
        string worldId, CancellationToken ct = default);

    Task SaveAsync(
        string worldId,
        IReadOnlyList<PlayerRosterEntry> entries,
        CancellationToken ct = default);
}
