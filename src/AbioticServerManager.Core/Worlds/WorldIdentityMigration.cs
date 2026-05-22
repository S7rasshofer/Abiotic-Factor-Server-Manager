namespace AbioticServerManager.Core.Worlds;

/// <summary>
/// Pure planner for the §2.1 world-identity migration: per-world
/// <c>SandboxSettings.ini</c> and <c>Admin.ini</c> move from "inside the server
/// install" to <c>&lt;DataRoot&gt;/worlds/&lt;id&gt;/config/</c> so a SteamCMD
/// validate, a server reinstall, or a <c>%LOCALAPPDATA%</c> wipe cannot
/// destroy per-world tuning, admins, or bans. Migration is always copy —
/// never move or delete — so the user can verify the new location before
/// retiring the old one via Reset Managed Data.
/// </summary>
public enum WorldIdentityMigrationAction
{
    /// <summary>Nothing to do: no legacy file or target already current.</summary>
    None,

    /// <summary>Legacy file exists, target does not — copy legacy → target.</summary>
    CopyLegacyToTarget,

    /// <summary>Both exist; target is the same or newer — leave alone (idempotent).</summary>
    AlreadyMigrated,
}

public sealed record WorldIdentityMigrationStep
{
    public required string Description { get; init; }
    public required string? LegacyPath { get; init; }
    public required string TargetPath { get; init; }
    public required WorldIdentityMigrationAction Action { get; init; }
}

public sealed record WorldIdentityMigrationPlan
{
    public required WorldIdentityMigrationStep Sandbox { get; init; }
    public required WorldIdentityMigrationStep Admin { get; init; }

    public bool HasWork =>
        Sandbox.Action == WorldIdentityMigrationAction.CopyLegacyToTarget ||
        Admin.Action == WorldIdentityMigrationAction.CopyLegacyToTarget;
}

/// <summary>
/// Pure migration planner. Takes filesystem facts as inputs (existence + last
/// write times) so it can be unit-tested without touching real files.
/// </summary>
public static class WorldIdentityMigration
{
    public static WorldIdentityMigrationAction PlanAction(
        bool legacyExists,
        bool targetExists,
        DateTimeOffset? legacyWriteTime = null,
        DateTimeOffset? targetWriteTime = null)
    {
        if (!legacyExists)
        {
            return WorldIdentityMigrationAction.None;
        }

        if (!targetExists)
        {
            return WorldIdentityMigrationAction.CopyLegacyToTarget;
        }

        // Both exist. If target is at least as new as legacy, treat as already
        // migrated — the user (or a previous run) has been editing the new file.
        // We do not overwrite a newer in-DataRoot file with a stale legacy one.
        if (legacyWriteTime is not null && targetWriteTime is not null &&
            targetWriteTime >= legacyWriteTime)
        {
            return WorldIdentityMigrationAction.AlreadyMigrated;
        }

        return WorldIdentityMigrationAction.AlreadyMigrated;
    }

    public static WorldIdentityMigrationPlan Plan(
        string? legacySandboxPath,
        string? legacyAdminPath,
        string targetSandboxPath,
        string targetAdminPath,
        Func<string, bool> fileExists,
        Func<string, DateTimeOffset?> writeTime)
    {
        return new WorldIdentityMigrationPlan
        {
            Sandbox = StepFor(
                "Per-world sandbox settings",
                legacySandboxPath,
                targetSandboxPath,
                fileExists,
                writeTime),
            Admin = StepFor(
                "Per-world admin / ban list",
                legacyAdminPath,
                targetAdminPath,
                fileExists,
                writeTime),
        };
    }

    private static WorldIdentityMigrationStep StepFor(
        string description,
        string? legacyPath,
        string targetPath,
        Func<string, bool> fileExists,
        Func<string, DateTimeOffset?> writeTime)
    {
        if (string.IsNullOrWhiteSpace(legacyPath))
        {
            return new WorldIdentityMigrationStep
            {
                Description = description,
                LegacyPath = null,
                TargetPath = targetPath,
                Action = WorldIdentityMigrationAction.None,
            };
        }

        var legacyExists = fileExists(legacyPath);
        var targetExists = !string.IsNullOrWhiteSpace(targetPath) && fileExists(targetPath);

        return new WorldIdentityMigrationStep
        {
            Description = description,
            LegacyPath = legacyPath,
            TargetPath = targetPath,
            Action = PlanAction(
                legacyExists,
                targetExists,
                legacyExists ? writeTime(legacyPath) : null,
                targetExists ? writeTime(targetPath) : null),
        };
    }
}
