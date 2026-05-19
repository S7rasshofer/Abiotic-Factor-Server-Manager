using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Runtime;

public interface ILaunchArgumentBuilder
{
    /// <summary>
    /// Builds the ordered launch tokens for the dedicated server. String values that
    /// contain spaces are wrapped in the Unreal-style <c>-Key="value"</c> form. Empty
    /// optional arguments are omitted.
    /// </summary>
    IReadOnlyList<string> BuildArguments(ServerInstance instance);

    /// <summary>
    /// The same arguments joined into one command line, with secrets replaced by
    /// <c>********</c>. Safe to write to logs and the diagnostics panel.
    /// </summary>
    string BuildMaskedCommandLine(ServerInstance instance);
}
