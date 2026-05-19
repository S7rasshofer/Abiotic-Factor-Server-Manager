namespace AbioticServerManager.Core.Backup;

/// <summary>
/// One on-disk backup of a world. The folder name is the timestamp id; the manifest
/// inside records what was actually captured so a restore is honest about gaps
/// (e.g. a config-only backup taken before the world save existed).
/// </summary>
public sealed record BackupEntry
{
    /// <summary>Folder name, sortable timestamp id (e.g. <c>20260518-143012</c>).</summary>
    public required string Id { get; init; }

    /// <summary>Absolute path to the backup folder.</summary>
    public required string Path { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    /// <summary>Why the backup was taken (e.g. <c>manual</c>, <c>before-delete</c>).</summary>
    public required string Reason { get; init; }

    public long SizeBytes { get; init; }

    public bool IncludedWorldSave { get; init; }
    public bool IncludedSandboxIni { get; init; }
    public bool IncludedAdminIni { get; init; }

    public string SizeText => SizeBytes switch
    {
        >= 1024L * 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024 * 1024):0.##} GB",
        >= 1024 * 1024 => $"{SizeBytes / (1024.0 * 1024):0.##} MB",
        >= 1024 => $"{SizeBytes / 1024.0:0.##} KB",
        _ => $"{SizeBytes} B",
    };
}
