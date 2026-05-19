namespace AbioticServerManager.Core.Install;

public enum ServerInstallKind
{
    Missing,
    EmptyFolder,
    InvalidFolder,
    DetectedUnmanaged,
    SteamCmdManaged,
}
