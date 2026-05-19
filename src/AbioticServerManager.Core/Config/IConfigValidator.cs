using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Config;

public interface IConfigValidator
{
    /// <summary>
    /// Validates a single instance against the rules in the implementation plan
    /// (section 12.1). <paramref name="otherInstances"/> is used for cross-world port
    /// conflict detection; pass an empty list when validating in isolation.
    /// </summary>
    IReadOnlyList<DiagnosticMessage> Validate(
        ServerInstance instance,
        IReadOnlyList<ServerInstance> otherInstances);
}
