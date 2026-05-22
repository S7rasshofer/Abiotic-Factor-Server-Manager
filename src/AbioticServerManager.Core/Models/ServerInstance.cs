namespace AbioticServerManager.Core.Models;

/// <summary>
/// One managed Abiotic Factor world profile. Maps to a single horizontal world tab.
/// Persisted as JSON; kept as a plain mutable POCO so the persistence layer and the
/// MVVM layer can each own their own concerns.
/// </summary>
public sealed class ServerInstance
{
    private ServerInstanceNetworkState? _network = new();

    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>Friendly label shown in the app's horizontal tab.</summary>
    public string DisplayName { get; set; } = "New World";

    /// <summary>Name shown in the Abiotic Factor server browser (-SteamServerName).</summary>
    public string SteamServerName { get; set; } = "My Abiotic Server";

    /// <summary>Save folder identity used by -WorldSaveName. Distinct from the browser name.</summary>
    public string WorldSaveName { get; set; } = "Cascade";

    public string ServerPassword { get; set; } = "";
    public string AdminPassword { get; set; } = "";

    public int MaxPlayers { get; set; } = 6;
    public int GamePort { get; set; } = 7777;
    public int QueryPort { get; set; } = 27015;

    public bool LanOnly { get; set; }
    public bool UseLocalIps { get; set; }
    public PlatformAccessMode PlatformAccessMode { get; set; } = PlatformAccessMode.All;

    public string? MultiHomeAddress { get; set; }

    public ServerInstanceNetworkState Network
    {
        get => _network ??= new ServerInstanceNetworkState();
        set => _network = value ?? new ServerInstanceNetworkState();
    }

    public string InstallPath { get; set; } = "";
    public string WorldPath { get; set; } = "";
    public string SandboxIniPath { get; set; } = "";
    public string AdminIniPath { get; set; } = "";

    /// <summary>
    /// Raw/advanced launch arguments the app does not model explicitly. Preserved verbatim
    /// so future game versions do not lose user customisation. "Discover it, do not hardcode it."
    /// </summary>
    public List<string> AdditionalLaunchArguments { get; set; } = [];

    public ServerInstance Clone()
    {
        var clone = (ServerInstance)MemberwiseClone();
        clone.AdditionalLaunchArguments = [.. AdditionalLaunchArguments];
        clone.Network = Network.Clone();
        return clone;
    }
}

public sealed class ServerInstanceNetworkState
{
    public string? LastDetectedLanIpv4 { get; set; }
    public DateTimeOffset? LastFirewallRepairAtUtc { get; set; }

    public ServerInstanceNetworkState Clone() => new()
    {
        LastDetectedLanIpv4 = LastDetectedLanIpv4,
        LastFirewallRepairAtUtc = LastFirewallRepairAtUtc,
    };
}
