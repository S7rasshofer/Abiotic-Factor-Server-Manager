namespace AbioticServerManager.Core.Backup;

public sealed record BackupResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }
    public BackupEntry? Entry { get; init; }

    public static BackupResult Ok(string message, BackupEntry? entry = null) =>
        new() { Success = true, Message = message, Entry = entry };

    public static BackupResult Fail(string message) =>
        new() { Success = false, Message = message };
}
