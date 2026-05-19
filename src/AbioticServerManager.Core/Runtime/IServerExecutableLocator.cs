namespace AbioticServerManager.Core.Runtime;

/// <summary>
/// Finds the dedicated server executable inside an install directory by discovery rather
/// than a hardcoded path, so a game update that relocates or renames the binary does not
/// break launching. "Do not hardcode the facility. Discover it."
/// </summary>
public interface IServerExecutableLocator
{
    /// <summary>
    /// Returns the best-guess server executable path under <paramref name="installPath"/>,
    /// or null if none can be found.
    /// </summary>
    string? Locate(string installPath);
}

public sealed class ServerExecutableLocator : IServerExecutableLocator
{
    public string? Locate(string installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath) || !Directory.Exists(installPath))
        {
            return null;
        }

        var candidates = Directory
            .EnumerateFiles(installPath, "*.exe", SearchOption.AllDirectories)
            .Where(p => Path.GetFileName(p).Contains("server", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // Prefer the shipping Win64 server binary, then anything under Binaries, then the
        // shortest path (least likely to be a helper/tool).
        return candidates
            .OrderByDescending(p => p.Contains("Shipping", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(p => p.Contains("Win64", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(p => p.Contains("Binaries", StringComparison.OrdinalIgnoreCase))
            .ThenBy(p => p.Length)
            .First();
    }
}
