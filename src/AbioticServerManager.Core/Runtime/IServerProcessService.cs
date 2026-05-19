using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Runtime;

/// <summary>
/// Launches the dedicated server executable directly (never via a batch file) so the app
/// gets safe argument construction, log capture, crash detection and clean stop/restart.
/// </summary>
public interface IServerProcessService
{
    /// <summary>Raised for every captured stdout/stderr line of any running server.</summary>
    event EventHandler<ServerLogLine>? LogReceived;

    /// <summary>Raised when an instance starts, stops or crashes.</summary>
    event EventHandler<string>? RuntimeChanged;

    bool IsRunning(string instanceId);

    Task<ServerStartResult> StartAsync(ServerInstance instance, CancellationToken ct = default);

    Task StopAsync(ServerInstance instance, CancellationToken ct = default);

    Task<ServerStartResult> RestartAsync(ServerInstance instance, CancellationToken ct = default);
}
