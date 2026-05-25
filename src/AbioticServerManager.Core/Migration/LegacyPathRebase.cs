using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Migration;

/// <summary>
/// Re-points an imported legacy <see cref="ServerInstance"/> at paths that
/// actually exist on the current machine. Pre-§2.1 records carry absolute
/// install / sandbox / admin paths from the old layout; after a user deletes
/// the old folders, those paths are stale and Start fails in confusing ways.
/// This is a copy-not-delete-friendly scrub: it never touches the source,
/// only the imported record. Pure so it can be unit-tested without IO.
/// </summary>
public static class LegacyPathRebase
{
    /// <summary>
    /// Returns a clone of <paramref name="instance"/> with stale absolute paths
    /// either reset to empty (so the rest of the app's canonical-fallback logic
    /// resolves them under the current data root) or, for the install path,
    /// replaced with <paramref name="defaultInstallDirectory"/>. A path is
    /// considered stale only when the supplied probe says it does not exist;
    /// valid customisations (e.g. an adopted external install that's still on
    /// disk) are left alone.
    /// </summary>
    public static ServerInstance ScrubStalePaths(
        ServerInstance instance,
        string defaultInstallDirectory,
        Func<string, bool> directoryExists,
        Func<string, bool> fileExists)
    {
        var clone = instance.Clone();

        if (!string.IsNullOrWhiteSpace(clone.InstallPath) && !directoryExists(clone.InstallPath))
        {
            clone.InstallPath = defaultInstallDirectory ?? "";
        }

        if (!string.IsNullOrWhiteSpace(clone.WorldPath) && !directoryExists(clone.WorldPath))
        {
            clone.WorldPath = "";
        }

        if (!string.IsNullOrWhiteSpace(clone.SandboxIniPath) && !fileExists(clone.SandboxIniPath))
        {
            clone.SandboxIniPath = "";
        }

        if (!string.IsNullOrWhiteSpace(clone.AdminIniPath) && !fileExists(clone.AdminIniPath))
        {
            clone.AdminIniPath = "";
        }

        return clone;
    }
}
