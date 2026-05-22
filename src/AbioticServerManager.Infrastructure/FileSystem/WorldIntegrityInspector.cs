using AbioticServerManager.Core.Config;
using AbioticServerManager.Core.Install;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Services;
using AbioticServerManager.Core.Worlds;

namespace AbioticServerManager.Infrastructure.FileSystem;

/// <summary>
/// §4.5: collects a world's filesystem facts and hands them to the pure
/// <see cref="WorldIntegrityValidator"/>. The IO lives here so the verdict
/// stays a unit-testable Core function.
/// </summary>
public sealed class WorldIntegrityInspector : IWorldIntegrityInspector
{
    private readonly IAppPaths _paths;
    private readonly IServerInstallStateService _installState;

    public WorldIntegrityInspector(IAppPaths paths, IServerInstallStateService installState)
    {
        _paths = paths;
        _installState = installState;
    }

    public WorldIntegrityReport Inspect(ServerInstance instance)
    {
        // The world's *expected* config paths: whatever the world currently
        // points at, falling back to the canonical <DataRoot>/worlds/<id>/
        // location so a never-saved world is "doesn't exist yet" (a warning),
        // not "path unset" (a blocker).
        var sandboxPath = ResolvePath(instance.SandboxIniPath, _paths.WorldSandboxIniPath(instance.Id));
        var adminPath = ResolvePath(instance.AdminIniPath, _paths.WorldAdminIniPath(instance.Id));
        var sandboxExists = SafeFileExists(sandboxPath);

        var inputs = new WorldIntegrityInputs
        {
            SandboxIniPath = sandboxPath,
            SandboxIniExists = sandboxExists,
            SandboxIniParses = !sandboxExists || CanParseIni(sandboxPath),
            AdminIniPath = adminPath,
            AdminIniExists = SafeFileExists(adminPath),
            WorldSaveFolderResolvable =
                !string.IsNullOrWhiteSpace(WorldSaveLayout.ExpectedWorldFolder(instance)),
            SandboxUnderDataRoot = IsUnderWorldsDirectory(sandboxPath),
            ServerExecutableFound = !string.IsNullOrWhiteSpace(_installState.Evaluate(instance).ExecutablePath),
        };

        return WorldIntegrityValidator.Validate(inputs);
    }

    private static string ResolvePath(string configured, string canonicalFallback) =>
        string.IsNullOrWhiteSpace(configured) ? canonicalFallback : configured;

    private static bool SafeFileExists(string path)
    {
        try
        {
            return !string.IsNullOrWhiteSpace(path) && File.Exists(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool CanParseIni(string path)
    {
        try
        {
            IniDocument.Parse(File.ReadAllText(path));
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // An unreadable / locked file is the realistic "corrupt" case —
            // the loss-less INI parser itself never throws on bad content.
            return false;
        }
    }

    private bool IsUnderWorldsDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        try
        {
            var full = Path.GetFullPath(path);
            var worldsRoot = Path.GetFullPath(_paths.WorldsDirectory);
            if (!worldsRoot.EndsWith(Path.DirectorySeparatorChar))
            {
                worldsRoot += Path.DirectorySeparatorChar;
            }

            return full.StartsWith(worldsRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is ArgumentException or IOException)
        {
            return false;
        }
    }
}
