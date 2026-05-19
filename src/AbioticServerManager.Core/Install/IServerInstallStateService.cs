using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Install;

public interface IServerInstallStateService
{
    ServerInstallState Evaluate(ServerInstance instance);

    ServerInstallState Evaluate(string? installPath);
}
