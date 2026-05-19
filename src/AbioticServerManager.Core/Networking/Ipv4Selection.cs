namespace AbioticServerManager.Core.Networking;

/// <summary>
/// A single candidate LAN IPv4, described by facts the Windows layer can gather
/// from a network adapter. Kept free of <c>System.Net.NetworkInformation</c> types
/// so the ranking rules are unit-testable.
/// </summary>
public sealed record Ipv4Candidate
{
    public required string Address { get; init; }
    public required string InterfaceName { get; init; }
    public required string InterfaceDescription { get; init; }

    /// <summary>Adapter operational status is Up.</summary>
    public bool IsUp { get; init; }

    /// <summary>Adapter has a usable IPv4 default gateway (i.e. the default route).</summary>
    public bool HasDefaultGateway { get; init; }

    public string GatewayAddress { get; init; } = "";

    /// <summary>Adapter is a loopback or tunnel interface type.</summary>
    public bool IsLoopbackOrTunnel { get; init; }

    /// <summary>Interface metric for the default route; lower is preferred. Null if unknown.</summary>
    public int? RouteMetric { get; init; }
}

public sealed record Ipv4SelectionResult
{
    /// <summary>The best LAN IPv4 to hand the router checklist, or null if none.</summary>
    public string? Best { get; init; }

    /// <summary>Every usable LAN IPv4, best first.</summary>
    public IReadOnlyList<string> Usable { get; init; } = [];

    public IReadOnlyList<Ipv4Candidate> UsableCandidates { get; init; } = [];

    /// <summary>
    /// True when more than one equally-plausible physical LAN address exists, so
    /// the user should confirm which one their router should target.
    /// </summary>
    public bool HasAmbiguity { get; init; }

    public string? Warning { get; init; }
}

/// <summary>
/// Picks the LAN IPv4 a router should forward to. Prefers the active
/// default-route physical adapter and pushes virtual / VPN / container adapters
/// to the back so they only win when nothing else is available.
/// </summary>
public static class Ipv4Selection
{
    private static readonly string[] VirtualAdapterMarkers =
    [
        "hyper-v", "vethernet", "virtualbox", "vmware", "vmnet",
        "wsl", "docker", "default switch", "loopback", "tunnel",
        "tailscale", "zerotier", "wireguard", "openvpn", "tap-",
        "tap windows", "wan miniport", "hamachi", "radmin",
        "bluetooth", "npcap", "pppoe", "teredo", "isatap", " vpn",
    ];

    public static bool LooksVirtual(string interfaceName, string interfaceDescription)
    {
        var haystack = (interfaceName + " " + interfaceDescription).ToLowerInvariant();
        return VirtualAdapterMarkers.Any(marker =>
            haystack.Contains(marker, StringComparison.Ordinal));
    }

    public static Ipv4SelectionResult Select(IEnumerable<Ipv4Candidate> candidates)
    {
        var usable = candidates
            .Where(c =>
                c.IsUp &&
                !c.IsLoopbackOrTunnel &&
                IpAddressClassifier.IsUsableLanScope(IpAddressClassifier.Classify(c.Address)))
            .Select(c => new
            {
                Candidate = c,
                Virtual = LooksVirtual(c.InterfaceName, c.InterfaceDescription),
                Scope = IpAddressClassifier.Classify(c.Address),
            })
            .ToList();

        if (usable.Count == 0)
        {
            return new Ipv4SelectionResult
            {
                Warning = "No active LAN IPv4 address was detected. Connect this PC to your " +
                          "network (Ethernet or Wi-Fi) and run Check Setup again.",
            };
        }

        var ranked = usable
            // Real physical NIC with a default route is the ideal router target.
            .OrderByDescending(x => !x.Virtual && x.Candidate.HasDefaultGateway)
            .ThenByDescending(x => x.Candidate.HasDefaultGateway)
            .ThenByDescending(x => !x.Virtual)
            // A normal home LAN is RFC1918; CGNAT/public on a NIC is less likely the target.
            .ThenByDescending(x => x.Scope == Ipv4Scope.PrivateRfc1918)
            .ThenBy(x => x.Candidate.RouteMetric ?? int.MaxValue)
            .ThenBy(x => x.Candidate.Address, StringComparer.Ordinal)
            .ToList();

        var orderedCandidates = ranked
            .GroupBy(x => x.Candidate.Address, StringComparer.Ordinal)
            .Select(g => g.First().Candidate)
            .ToList();
        var ordered = orderedCandidates.Select(c => c.Address).ToList();

        // Ambiguity = more than one physical, gateway-bearing candidate. Virtual-only
        // overlap is not worth nagging the user about.
        var plausible = ranked
            .Where(x => !x.Virtual && x.Candidate.HasDefaultGateway)
            .Select(x => x.Candidate.Address)
            .Distinct()
            .ToList();

        var ambiguous = plausible.Count > 1;
        return new Ipv4SelectionResult
        {
            Best = ordered[0],
            Usable = ordered,
            UsableCandidates = orderedCandidates,
            HasAmbiguity = ambiguous,
            Warning = ambiguous
                ? "More than one physical network connection was found (" +
                  string.Join(", ", plausible) +
                  "). Confirm which one your router forwards to before testing."
                : null,
        };
    }
}
