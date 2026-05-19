namespace AbioticServerManager.Core.Models;

/// <summary>
/// Platform access filter for the dedicated server. The unrestricted default omits
/// <c>-PlatformLimited</c>, keeping crossplay enabled.
/// </summary>
public enum PlatformAccessMode
{
    All = 0,
    PcOnly = 1,
    PlaystationOnly = 2,
    XboxOnly = 3,
}
