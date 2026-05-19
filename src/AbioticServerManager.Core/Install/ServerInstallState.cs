namespace AbioticServerManager.Core.Install;

public sealed record ServerInstallState
{
    public required ServerInstallKind Kind { get; init; }
    public required string DataRoot { get; init; }
    public required string ServerInstallPath { get; init; }
    public string? ExecutablePath { get; init; }
    public string? ManifestPath { get; init; }
    public string AppId { get; init; } = ISteamCmdService.AbioticFactorDedicatedAppId;
    public string? BuildId { get; init; }
    public DateTimeOffset? LastUpdated { get; init; }
    public required string InstallSource { get; init; }
    public required string ValidationMessage { get; init; }

    public bool IsLaunchable =>
        Kind is ServerInstallKind.DetectedUnmanaged or ServerInstallKind.SteamCmdManaged;
}
