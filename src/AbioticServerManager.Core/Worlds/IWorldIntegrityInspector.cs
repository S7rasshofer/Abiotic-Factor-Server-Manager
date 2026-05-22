using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Worlds;

/// <summary>
/// Gathers a world's on-disk facts (config presence, parseability, install
/// location) and runs them through the pure <see cref="WorldIntegrityValidator"/>.
/// Implemented in Infrastructure: collecting the facts is the only IO step;
/// the verdict itself stays a pure Core function.
/// </summary>
public interface IWorldIntegrityInspector
{
    /// <summary>Inspects one world and returns its pre-start integrity report.</summary>
    WorldIntegrityReport Inspect(ServerInstance instance);
}
