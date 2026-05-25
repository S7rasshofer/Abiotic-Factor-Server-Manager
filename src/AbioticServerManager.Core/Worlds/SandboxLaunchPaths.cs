using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Worlds;

/// <summary>
/// Resolves the three paths involved in launching a world with the correct
/// sandbox + admin settings:
/// <list type="bullet">
/// <item><b>Durable</b>: <c>&lt;DataRoot&gt;/worlds/&lt;id&gt;/config/...</c> -
/// the user's source of truth that the Sandbox tab writes to. Survives
/// reinstalls of the dedicated server, and is what backups capture.</item>
/// <item><b>Staged</b>: a copy under <c>&lt;install&gt;/AbioticFactor/Saved/Config/FacilityOverseer/&lt;id&gt;/...</c>
/// placed there at launch time by <c>SandboxRuntimeStagingService</c>.
/// This is what the running AF process actually reads from.</item>
/// <item><b>RelativeArg</b>: the path relative to <c>&lt;install&gt;/AbioticFactor/Saved/</c>
/// that gets passed as <c>-SandboxIniPath=</c> / <c>-AdminIniPath=</c>. AF's
/// dedicated server hard-prefixes the value with <c>../../../AbioticFactor/Saved/</c>
/// before resolving it, so absolute Windows paths produce the malformed
/// lookup <c>../../../AbioticFactor/Saved/C:\Users\...\SandboxSettings.ini</c>
/// (the error that drove this design).</item>
/// </list>
/// Pure: takes a <see cref="ServerInstance"/> and returns paths.
/// IO (copy/sync) lives in <c>SandboxRuntimeStagingService</c>.
/// </summary>
public sealed record SandboxLaunchPaths(
    string DurableSandboxPath,
    string DurableAdminPath,
    string StagedSandboxPath,
    string StagedAdminPath,
    string StagedFolder,
    string RelativeSandboxArg,
    string RelativeAdminArg)
{
    /// <summary>
    /// Builds the resolver for the given world. Throws when the install path
    /// is missing - staging needs a place to copy to and the launch arg
    /// builder needs an anchor for the relative computation.
    /// </summary>
    public static SandboxLaunchPaths For(ServerInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.InstallPath))
        {
            throw new InvalidOperationException(
                "Cannot resolve sandbox launch paths: ServerInstance.InstallPath is empty.");
        }

        var stagedFolder = WorldSaveLayout.StagedConfigFolder(instance);
        var stagedSandbox = WorldSaveLayout.StagedSandboxPath(instance);
        var stagedAdmin = WorldSaveLayout.StagedAdminPath(instance);

        var savedRoot = Path.Combine(instance.InstallPath, "AbioticFactor", "Saved");

        return new SandboxLaunchPaths(
            DurableSandboxPath: instance.SandboxIniPath ?? string.Empty,
            DurableAdminPath: instance.AdminIniPath ?? string.Empty,
            StagedSandboxPath: stagedSandbox,
            StagedAdminPath: stagedAdmin,
            StagedFolder: stagedFolder,
            RelativeSandboxArg: ToRelativeArg(savedRoot, stagedSandbox),
            RelativeAdminArg: ToRelativeArg(savedRoot, stagedAdmin));
    }

    private static string ToRelativeArg(string savedRoot, string absolutePath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath))
        {
            return string.Empty;
        }

        // AF uses forward slashes regardless of OS - keep the arg portable.
        return Path.GetRelativePath(savedRoot, absolutePath).Replace('\\', '/');
    }
}
