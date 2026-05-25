namespace AbioticServerManager.Core.Networking;

/// <summary>
/// Inputs collected by the App/Infrastructure to compose router-agnostic guidance
/// for pinning the host's LAN IPv4 via DHCP reservation. Pure record so the
/// composition is unit-testable.
/// </summary>
public sealed record LanIpLockContext
{
    public required string Ipv4 { get; init; }
    public string Hostname { get; init; } = "";
    public string MacAddress { get; init; } = "";
    public string Gateway { get; init; } = "";
    public string AdapterDescription { get; init; } = "";
}

/// <summary>
/// Composes "lock this LAN IP" copy/paste guidance from <see cref="LanIpLockContext"/>.
/// Router-agnostic: every consumer router (UniFi, ASUS, Netgear, TP-Link, ISP
/// gateways) calls this "DHCP Reservation" or "Static Lease", so we explain the
/// concept and provide the values the user will need to paste.
/// </summary>
public static class LanIpLockGuidance
{
    public static string Compose(LanIpLockContext ctx)
    {
        if (string.IsNullOrWhiteSpace(ctx.Ipv4))
        {
            return
                "No LAN IPv4 detected yet. Connect this PC to your network (Ethernet " +
                "or Wi-Fi), then re-open this card to see the values your router needs.";
        }

        var lines = new List<string>
        {
            $"To stop your LAN IP from changing (and breaking port forwarding), add a",
            $"DHCP reservation in your router that ties this PC to {ctx.Ipv4}.",
            "",
            "Values to paste into your router's DHCP Reservation / Static Lease page:",
        };

        lines.Add($"  - Reserved IP:   {ctx.Ipv4}");

        if (!string.IsNullOrWhiteSpace(ctx.MacAddress))
        {
            lines.Add($"  - MAC address:   {ctx.MacAddress}");
        }
        else
        {
            lines.Add("  - MAC address:   (not detected - see Windows: Settings -> Network -> Properties)");
        }

        if (!string.IsNullOrWhiteSpace(ctx.Hostname))
        {
            lines.Add($"  - Hostname:      {ctx.Hostname}");
        }

        if (!string.IsNullOrWhiteSpace(ctx.AdapterDescription))
        {
            lines.Add($"  - Adapter:       {ctx.AdapterDescription}");
        }

        if (!string.IsNullOrWhiteSpace(ctx.Gateway))
        {
            lines.Add("");
            lines.Add($"Router admin page: http://{ctx.Gateway}/  (your default gateway)");
        }

        lines.Add("");
        lines.Add("Where to look in your router (typical names):");
        lines.Add("  - UniFi:          Settings -> Networks -> (your LAN) -> DHCP -> Static Leases");
        lines.Add("  - ASUS:           LAN -> DHCP Server -> Manually Assigned IP");
        lines.Add("  - Netgear:        Advanced -> Setup -> LAN Setup -> Address Reservation");
        lines.Add("  - TP-Link:        Network -> DHCP Server -> Address Reservation");
        lines.Add("  - Most ISP boxes: \"DHCP Reservation\" or \"Static Lease\" under LAN/DHCP");
        lines.Add("");
        lines.Add(
            "After saving the reservation, reboot this PC (or release/renew DHCP) so " +
            "it picks up the locked address.");

        return string.Join('\n', lines);
    }
}
