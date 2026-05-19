namespace AbioticServerManager.Core.Networking;

/// <summary>
/// Builds the copy-paste router port-forwarding checklist. Pure so tests can
/// assert it always uses the selected ports and the detected LAN IPv4 and never
/// tells the user to forward anything else.
/// </summary>
public static class RouterChecklistBuilder
{
    public static IReadOnlyList<string> Build(int gamePort, int queryPort, string? lanIpv4) =>
        Build("Selected world", gamePort, queryPort, lanIpv4);

    public static IReadOnlyList<string> Build(
        string worldName,
        int gamePort,
        int queryPort,
        string? lanIpv4)
    {
        var target = string.IsNullOrWhiteSpace(lanIpv4)
            ? "<this PC's LAN IPv4 - run Check Setup to detect it>"
            : lanIpv4;

        var lines = new List<string>
        {
            "Abiotic Factor router port forwarding",
            "",
            $"Selected world: {worldName}",
            $"Server PC LAN IPv4: {target}",
            "",
        };

        if (gamePort == queryPort)
        {
            lines.Add(
                "WARNING: Game port and query port are the same. Set them to different " +
                "values on the Server tab before forwarding.");
            lines.Add("");
        }

        lines.Add("Create these UDP forwards:");
        lines.Add($"- External UDP {gamePort} -> {target}:{gamePort}  (game)");
        lines.Add($"- External UDP {queryPort} -> {target}:{queryPort}  (server browser query)");
        lines.Add("");
        lines.Add("Important:");
        lines.Add("- Use UDP, not TCP.");
        lines.Add("- External and internal ports should match one-to-one.");
        lines.Add($"- Reserve {target} in your router's DHCP settings so it does not change.");
        lines.Add("- If this PC's LAN IP changes, update the router forwards.");
        lines.Add("- Do not forward unrelated ports such as Remote Desktop, SMB, or router admin.");
        lines.Add(
            "- If your router WAN IP is private, 100.64.0.0/10, or differs from your public IP, " +
            "you may be behind CGNAT/double NAT and normal port forwarding may not work.");
        lines.Add("");
        lines.Add("After saving router changes:");
        lines.Add("- Start the server.");
        lines.Add(
            "- Ask someone outside your network to find it in the server browser or " +
            "connect directly.");
        lines.Add(
            "- If it appears but they cannot connect, the game UDP port may be blocked or " +
            "forwarded to the wrong LAN IP.");
        lines.Add(
            "- If it does not appear at all, the query UDP port may be blocked or forwarded " +
            "to the wrong LAN IP.");

        return lines;
    }
}
