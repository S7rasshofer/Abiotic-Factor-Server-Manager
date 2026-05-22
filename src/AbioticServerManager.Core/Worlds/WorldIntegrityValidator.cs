namespace AbioticServerManager.Core.Worlds;

/// <summary>
/// Severity of a single world-integrity finding.
/// </summary>
public enum WorldIntegritySeverity
{
    /// <summary>Informational — nothing to block on.</summary>
    Info,

    /// <summary>Likely to bite the user if not addressed, but not a hard block.</summary>
    Warning,

    /// <summary>Hard block — Start should not be enabled until the user resolves it.</summary>
    Blocker,
}

/// <summary>One row in the integrity report. Stable Id so the UI can dedupe across refreshes.</summary>
public sealed record WorldIntegrityFinding
{
    public required string Id { get; init; }
    public required WorldIntegritySeverity Severity { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public string SuggestedFix { get; init; } = "";
}

public sealed record WorldIntegrityReport
{
    public required IReadOnlyList<WorldIntegrityFinding> Findings { get; init; }

    /// <summary>True when no <see cref="WorldIntegritySeverity.Blocker"/> findings are present.</summary>
    public bool IsLaunchable { get; init; }
}

/// <summary>
/// Inputs gathered by the App/Infrastructure for one world. Reduced to value
/// types so validation is pure and unit-testable without filesystem IO.
/// </summary>
public sealed record WorldIntegrityInputs
{
    /// <summary>The world's expected sandbox INI absolute path. Empty = unset.</summary>
    public required string SandboxIniPath { get; init; }

    /// <summary>True when <see cref="SandboxIniPath"/> exists on disk.</summary>
    public required bool SandboxIniExists { get; init; }

    /// <summary>True when <see cref="SandboxIniPath"/> parses successfully (no INI corruption).</summary>
    public required bool SandboxIniParses { get; init; }

    /// <summary>The world's expected admin INI absolute path. Empty = unset.</summary>
    public required string AdminIniPath { get; init; }

    /// <summary>True when <see cref="AdminIniPath"/> exists on disk.</summary>
    public required bool AdminIniExists { get; init; }

    /// <summary>True when the world's expected save folder is present (or will be auto-created).</summary>
    public required bool WorldSaveFolderResolvable { get; init; }

    /// <summary>True when the sandbox ini lives under <c>&lt;DataRoot&gt;/worlds/&lt;id&gt;/</c>.</summary>
    public required bool SandboxUnderDataRoot { get; init; }

    /// <summary>True when the dedicated-server executable can be located.</summary>
    public required bool ServerExecutableFound { get; init; }
}

/// <summary>
/// Pre-start gate. Produces a deterministic report of integrity issues so the
/// app can show concrete blockers/warnings before the user hits Start, instead
/// of surfacing them as log errors during launch.
/// </summary>
public static class WorldIntegrityValidator
{
    public static WorldIntegrityReport Validate(WorldIntegrityInputs inputs)
    {
        var findings = new List<WorldIntegrityFinding>();

        if (!inputs.ServerExecutableFound)
        {
            findings.Add(new WorldIntegrityFinding
            {
                Id = "EXE_MISSING",
                Severity = WorldIntegritySeverity.Blocker,
                Title = "Server executable not found",
                Detail = "Facility Overseer could not locate AbioticFactorServer-Win64-Shipping.exe.",
                SuggestedFix = "Click Prepare / Update Server to install or repair the dedicated server.",
            });
        }

        if (string.IsNullOrWhiteSpace(inputs.SandboxIniPath))
        {
            findings.Add(new WorldIntegrityFinding
            {
                Id = "SANDBOX_PATH_UNSET",
                Severity = WorldIntegritySeverity.Blocker,
                Title = "Sandbox settings path is not set",
                Detail = "No per-world SandboxSettings.ini path has been resolved.",
                SuggestedFix = "Reopen the app — the §2.1 world-identity migration should set this on next load.",
            });
        }
        else
        {
            if (!inputs.SandboxIniExists)
            {
                findings.Add(new WorldIntegrityFinding
                {
                    Id = "SANDBOX_INI_MISSING",
                    Severity = WorldIntegritySeverity.Warning,
                    Title = "Sandbox settings file missing",
                    Detail = $"{inputs.SandboxIniPath} does not exist yet.",
                    SuggestedFix = "Open the World / Player / Enemy tabs and Save Sandbox to generate the file.",
                });
            }
            else if (!inputs.SandboxIniParses)
            {
                findings.Add(new WorldIntegrityFinding
                {
                    Id = "SANDBOX_INI_UNPARSEABLE",
                    Severity = WorldIntegritySeverity.Blocker,
                    Title = "Sandbox settings file cannot be parsed",
                    Detail = $"{inputs.SandboxIniPath} exists but contains invalid INI.",
                    SuggestedFix = "Restore the file from Backups, or delete it to regenerate defaults.",
                });
            }

            if (!inputs.SandboxUnderDataRoot)
            {
                findings.Add(new WorldIntegrityFinding
                {
                    Id = "SANDBOX_NOT_UNDER_DATAROOT",
                    Severity = WorldIntegritySeverity.Warning,
                    Title = "Sandbox settings live inside the server install",
                    Detail =
                        "A SteamCMD validate or server reinstall will destroy this file. " +
                        "The §2.1 migration moves it under <DataRoot>/worlds/<id>/config/.",
                    SuggestedFix = "Reopen the app to let the migration run, then verify the new path.",
                });
            }
        }

        if (string.IsNullOrWhiteSpace(inputs.AdminIniPath))
        {
            findings.Add(new WorldIntegrityFinding
            {
                Id = "ADMIN_PATH_UNSET",
                Severity = WorldIntegritySeverity.Info,
                Title = "Admin / ban list path is not set",
                Detail = "No per-world Admin.ini path has been resolved.",
                SuggestedFix = "This is fine for a fresh world — the file is created on first admin edit.",
            });
        }
        else if (!inputs.AdminIniExists)
        {
            findings.Add(new WorldIntegrityFinding
            {
                Id = "ADMIN_INI_MISSING",
                Severity = WorldIntegritySeverity.Info,
                Title = "Admin / ban list not created yet",
                Detail = $"{inputs.AdminIniPath} does not exist.",
                SuggestedFix = "This is fine for a fresh world; the file appears the first time you add an admin or ban.",
            });
        }

        if (!inputs.WorldSaveFolderResolvable)
        {
            findings.Add(new WorldIntegrityFinding
            {
                Id = "WORLD_SAVE_FOLDER_UNRESOLVABLE",
                Severity = WorldIntegritySeverity.Warning,
                Title = "World save folder location cannot be determined",
                Detail = "The dedicated-server save folder could not be resolved from current settings.",
                SuggestedFix = "Ensure the server is installed (Prepare / Update Server) and the world has a save name.",
            });
        }

        var hasBlocker = findings.Any(f => f.Severity == WorldIntegritySeverity.Blocker);
        return new WorldIntegrityReport
        {
            Findings = findings,
            IsLaunchable = !hasBlocker,
        };
    }
}
