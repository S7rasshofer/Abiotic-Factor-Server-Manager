using AbioticServerManager.Core.Models;
using AbioticServerManager.Core.Schema;

namespace AbioticServerManager.Core.Worlds;

/// <summary>
/// The single answer to "where is this world's sandbox config, where should a
/// new one go, and what should it be seeded from" - computed once so the flow
/// does not re-walk the filesystem or re-derive the same decision three times.
/// </summary>
public sealed record SandboxResolution
{
    /// <summary>True when Abiotic Factor has already created a playable save.</summary>
    public required bool RealSaveExists { get; init; }

    /// <summary>The real save folder if it exists, otherwise the expected location.</summary>
    public required string WorldFolder { get; init; }

    /// <summary>An existing sandbox file to load now, or null if one must be created.</summary>
    public required string? ExistingSandboxPath { get; init; }

    /// <summary>Where a new/default sandbox file should be written. May be empty when
    /// the install path is unknown; the caller supplies a data-root fallback.</summary>
    public required string DefaultSandboxTarget { get; init; }

    /// <summary>An existing file to copy from instead of the default template.</summary>
    public required string? MigrationSource { get; init; }
}

public static class WorldSaveLayout
{
    public const string StagedSandboxFolderName = "FacilityOverseer";

    public static string ExpectedWorldFolder(ServerInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.WorldPath) &&
            Directory.Exists(instance.WorldPath))
        {
            return Path.GetFullPath(instance.WorldPath);
        }

        if (string.IsNullOrWhiteSpace(instance.InstallPath))
        {
            return "";
        }

        return Path.Combine(
            instance.InstallPath,
            "AbioticFactor",
            "Saved",
            "SaveGames",
            "Server",
            "Worlds",
            WorldFolderName(instance));
    }

    public static string WorldSandboxPath(ServerInstance instance)
    {
        var folder = ExpectedWorldFolder(instance);
        return string.IsNullOrWhiteSpace(folder)
            ? ""
            : Path.Combine(folder, DefaultSandboxSettings.FileName);
    }

    /// <summary>
    /// The runtime staging folder for this world's INI files inside the
    /// server install: <c>&lt;install&gt;/AbioticFactor/Saved/Config/FacilityOverseer/&lt;world&gt;/</c>.
    /// Files copied here at launch are reachable by AF via a path relative
    /// to <c>&lt;install&gt;/AbioticFactor/Saved/</c> - the only form AF's
    /// <c>-SandboxIniPath</c> / <c>-AdminIniPath</c> launch args actually
    /// accept (AF hard-prefixes the value with <c>../../../AbioticFactor/Saved/</c>
    /// rather than honoring absolute Windows paths).
    /// </summary>
    public static string StagedConfigFolder(ServerInstance instance)
    {
        if (string.IsNullOrWhiteSpace(instance.InstallPath))
        {
            return "";
        }

        var folder = string.IsNullOrWhiteSpace(instance.Id)
            ? WorldFolderName(instance)
            : SanitizePathSegment(instance.Id);

        return Path.Combine(
            instance.InstallPath,
            "AbioticFactor",
            "Saved",
            "Config",
            StagedSandboxFolderName,
            folder);
    }

    public static string StagedSandboxPath(ServerInstance instance)
    {
        var folder = StagedConfigFolder(instance);
        return string.IsNullOrWhiteSpace(folder)
            ? ""
            : Path.Combine(folder, DefaultSandboxSettings.FileName);
    }

    /// <summary>
    /// Runtime staged path for this world's <c>Admin.ini</c>, parallel to
    /// <see cref="StagedSandboxPath"/>. Sits in the same per-world staged
    /// folder so a single Stage/SyncBack cycle covers both files.
    /// </summary>
    public static string StagedAdminPath(ServerInstance instance)
    {
        var folder = StagedConfigFolder(instance);
        return string.IsNullOrWhiteSpace(folder)
            ? ""
            : Path.Combine(folder, "Admin.ini");
    }

    public static bool IsRealWorldSaveFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return false;
        }

        try
        {
            // PlayerData is an O(1) directory probe; check it before the recursive
            // *.sav walk so the common "real save" case stays cheap.
            return Directory.Exists(Path.Combine(path, "PlayerData")) ||
                   Directory.EnumerateFiles(path, "*.sav", SearchOption.AllDirectories).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Locates the world's *actual* Abiotic Factor save folder rather than trusting
    /// a predicted name. The game derives the folder from <c>-WorldSaveName</c> and
    /// may not sanitise it exactly as we do, so we match known name candidates
    /// case-insensitively inside the shared <c>Worlds</c> container. Matching only
    /// known candidates (never "any real save") guarantees we cannot grab a
    /// different world's save that lives in the same container.
    /// </summary>
    public static string FindExistingRealWorldFolder(ServerInstance instance)
    {
        if (!string.IsNullOrWhiteSpace(instance.WorldPath) &&
            IsRealWorldSaveFolder(instance.WorldPath))
        {
            return Path.GetFullPath(instance.WorldPath);
        }

        if (string.IsNullOrWhiteSpace(instance.InstallPath))
        {
            return "";
        }

        var container = Path.Combine(
            instance.InstallPath,
            "AbioticFactor",
            "Saved",
            "SaveGames",
            "Server",
            "Worlds");
        if (!Directory.Exists(container))
        {
            return "";
        }

        var candidates = new[]
        {
            WorldFolderName(instance),
            SanitizePathSegment(instance.WorldSaveName),
            SanitizePathSegment(instance.DisplayName),
        }
        .Where(s => !string.IsNullOrWhiteSpace(s))
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToList();

        foreach (var candidate in candidates)
        {
            var predicted = Path.Combine(container, candidate);
            if (IsRealWorldSaveFolder(predicted))
            {
                return Path.GetFullPath(predicted);
            }
        }

        try
        {
            foreach (var dir in Directory.EnumerateDirectories(container))
            {
                var name = Path.GetFileName(dir);
                if (candidates.Any(c =>
                        string.Equals(c, name, StringComparison.OrdinalIgnoreCase)) &&
                    IsRealWorldSaveFolder(dir))
                {
                    return Path.GetFullPath(dir);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Treat an unreadable container as "no real save yet".
        }

        return "";
    }

    public static bool IsOrphanConfigOnlyWorldFolder(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) ||
            !Directory.Exists(path) ||
            IsRealWorldSaveFolder(path))
        {
            return false;
        }

        try
        {
            var entries = Directory.EnumerateFileSystemEntries(path).ToList();
            return entries.Count == 0 ||
                   entries.Count == 1 &&
                   File.Exists(entries[0]) &&
                   string.Equals(
                       Path.GetFileName(entries[0]),
                       DefaultSandboxSettings.FileName,
                       StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    public static string WorldFolderName(ServerInstance instance)
    {
        var source = string.IsNullOrWhiteSpace(instance.WorldSaveName)
            ? instance.DisplayName
            : instance.WorldSaveName;
        return SanitizePathSegment(source);
    }

    public static string SanitizePathSegment(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string([.. value.Where(c => !invalid.Contains(c))]).Trim();
        return cleaned.Length == 0 ? "World" : cleaned;
    }

    /// <summary>
    /// Computes the whole sandbox decision once. Behaviour is identical to the
    /// previous three separate code paths, just consolidated and with the real
    /// save located (not name-guessed).
    /// </summary>
    public static SandboxResolution Resolve(ServerInstance instance)
    {
        var realFolder = FindExistingRealWorldFolder(instance);
        var staged = StagedSandboxPath(instance);
        var legacy = instance.SandboxIniPath;

        if (!string.IsNullOrEmpty(realFolder))
        {
            var worldSandbox = Path.Combine(realFolder, DefaultSandboxSettings.FileName);
            string? migration =
                HasUsableFile(staged) && !PathEquals(staged, worldSandbox) ? staged :
                HasUsableFile(legacy) && !PathEquals(legacy, worldSandbox) &&
                    !PathEquals(legacy, staged) ? legacy :
                null;

            return new SandboxResolution
            {
                RealSaveExists = true,
                WorldFolder = realFolder,
                ExistingSandboxPath = File.Exists(worldSandbox) ? worldSandbox : null,
                DefaultSandboxTarget = worldSandbox,
                MigrationSource = migration,
            };
        }

        var expectedFolder = ExpectedWorldFolder(instance);
        var expectedSandbox = string.IsNullOrWhiteSpace(expectedFolder)
            ? ""
            : Path.Combine(expectedFolder, DefaultSandboxSettings.FileName);

        string? existing =
            HasUsableFile(staged) ? staged :
            HasUsableFile(legacy) && !PathEquals(legacy, expectedSandbox) ? legacy :
            null;

        return new SandboxResolution
        {
            RealSaveExists = false,
            WorldFolder = expectedFolder,
            ExistingSandboxPath = existing,
            DefaultSandboxTarget = staged,
            MigrationSource =
                HasUsableFile(legacy) && !PathEquals(legacy, staged) ? legacy : null,
        };
    }

    private static bool HasUsableFile(string? path) =>
        !string.IsNullOrWhiteSpace(path) && File.Exists(path);

    private static bool PathEquals(string? a, string? b) =>
        string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
