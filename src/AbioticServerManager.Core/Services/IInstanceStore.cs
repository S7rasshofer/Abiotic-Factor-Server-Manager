using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Services;

/// <summary>
/// Loads and saves the set of managed world profiles. Implementations must degrade
/// gracefully: a missing or corrupt store yields an empty list rather than throwing,
/// so a single bad file never bricks the app on startup.
/// </summary>
public interface IInstanceStore
{
    Task<IReadOnlyList<ServerInstance>> LoadAsync(CancellationToken ct = default);

    Task SaveAsync(IReadOnlyList<ServerInstance> instances, CancellationToken ct = default);
}
