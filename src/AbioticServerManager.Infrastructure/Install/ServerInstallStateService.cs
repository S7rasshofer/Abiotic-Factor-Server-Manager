using AbioticServerManager.Core.Install;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Core.Services;

namespace AbioticServerManager.Infrastructure.Install;

public sealed class ServerInstallStateService : IServerInstallStateService
{
    private readonly IAppPaths _paths;
    private readonly IServerExecutableLocator _locator;

    public ServerInstallStateService(IAppPaths paths, IServerExecutableLocator locator)
    {
        _paths = paths;
        _locator = locator;
    }

    public ServerInstallState Evaluate(ServerInstance instance) =>
        Evaluate(instance.InstallPath);

    public ServerInstallState Evaluate(string? installPath)
    {
        var path = string.IsNullOrWhiteSpace(installPath)
            ? _paths.ManagedServerDirectory
            : installPath;

        var fullPath = Path.GetFullPath(path);
        var installSource = IsManagedPath(fullPath) ? "Facility Overseer managed" : "External folder";

        if (!Directory.Exists(fullPath))
        {
            return State(
                ServerInstallKind.Missing,
                fullPath,
                installSource,
                "Server files have not been prepared yet.");
        }

        if (!Directory.EnumerateFileSystemEntries(fullPath).Any())
        {
            return State(
                ServerInstallKind.EmptyFolder,
                fullPath,
                installSource,
                "The server folder exists but is empty.");
        }

        var executable = _locator.Locate(fullPath);
        if (executable is null)
        {
            return State(
                ServerInstallKind.InvalidFolder,
                fullPath,
                installSource,
                "The selected folder does not contain the dedicated server executable.");
        }

        var manifest = FindManifest(fullPath);
        if (manifest is null)
        {
            return State(
                ServerInstallKind.DetectedUnmanaged,
                fullPath,
                installSource,
                "Dedicated server executable detected; update status is unknown.",
                executablePath: executable);
        }

        var buildId = TryReadBuildId(manifest);
        return State(
            ServerInstallKind.SteamCmdManaged,
            fullPath,
            installSource,
            buildId is { Length: > 0 }
                ? $"Dedicated server ready. Steam build {buildId}."
                : "Dedicated server ready. Steam manifest found.",
            executablePath: executable,
            manifestPath: manifest,
            buildId: buildId,
            lastUpdated: File.GetLastWriteTime(manifest));
    }

    private ServerInstallState State(
        ServerInstallKind kind,
        string installPath,
        string installSource,
        string validationMessage,
        string? executablePath = null,
        string? manifestPath = null,
        string? buildId = null,
        DateTimeOffset? lastUpdated = null) =>
        new()
        {
            Kind = kind,
            DataRoot = _paths.DataRoot,
            ServerInstallPath = installPath,
            ExecutablePath = executablePath,
            ManifestPath = manifestPath,
            BuildId = buildId,
            LastUpdated = lastUpdated,
            InstallSource = installSource,
            ValidationMessage = validationMessage,
        };

    private bool IsManagedPath(string installPath) =>
        string.Equals(
            TrimTrailingSeparator(installPath),
            TrimTrailingSeparator(Path.GetFullPath(_paths.ManagedServerDirectory)),
            StringComparison.OrdinalIgnoreCase);

    private static string TrimTrailingSeparator(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

    private static string? FindManifest(string installPath)
    {
        var expected = Path.Combine(
            installPath,
            "steamapps",
            $"appmanifest_{ISteamCmdService.AbioticFactorDedicatedAppId}.acf");
        return File.Exists(expected) ? expected : null;
    }

    private static string? TryReadBuildId(string manifestPath)
    {
        try
        {
            foreach (var line in File.ReadLines(manifestPath))
            {
                var trimmed = line.Trim();
                if (!trimmed.StartsWith("\"buildid\"", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var parts = trimmed.Split('"', StringSplitOptions.RemoveEmptyEntries);
                return parts.Length >= 2 ? parts[^1] : null;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }
}
