namespace AbioticServerManager.Core.Install;

/// <summary>
/// Manages SteamCMD and the Abiotic Factor dedicated server app (Steam app id 2857200)
/// via anonymous login. All long-running operations report progress and never block the
/// UI thread.
/// </summary>
public interface ISteamCmdService
{
    /// <summary>The Steam app id for the Abiotic Factor dedicated server.</summary>
    const string AbioticFactorDedicatedAppId = "2857200";

    Task<bool> IsSteamCmdInstalledAsync(CancellationToken ct = default);

    Task<SteamCmdResult> InstallSteamCmdAsync(
        IProgress<InstallProgress> progress,
        CancellationToken ct = default);

    Task<SteamCmdResult> InstallOrUpdateServerAsync(
        string installPath,
        IProgress<InstallProgress> progress,
        CancellationToken ct = default);

    Task<SteamCmdResult> ValidateServerAsync(
        string installPath,
        IProgress<InstallProgress> progress,
        CancellationToken ct = default);
}
