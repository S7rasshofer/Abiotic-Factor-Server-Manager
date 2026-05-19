using AbioticServerManager.Core.Diagnostics;

namespace AbioticServerManager.Core.Networking;

public sealed record PortValidationResult
{
    /// <summary>False blocks firewall rule creation (invalid range or identical ports).</summary>
    public required bool CanCreateRules { get; init; }
    public required IReadOnlyList<DiagnosticMessage> Messages { get; init; }
}

/// <summary>
/// Pure validation of the game/query port pair used for firewall rules and the
/// router checklist. Separate from <see cref="Config.ConfigValidator"/> so the
/// network feature can refuse to widen the firewall on a bad port without
/// pulling in unrelated server-config rules. "Already bound" is an IO concern
/// and is checked by the Windows layer, not here.
/// </summary>
public static class NetworkPortValidation
{
    public const int MinPort = 1;
    public const int MaxPort = 65535;
    public const int HighestPrivilegedPort = 1023;

    /// <summary>
    /// Well-known ports that should never be opened "for a game server". Opening
    /// these would widen the firewall far beyond what Abiotic Factor needs.
    /// </summary>
    private static readonly IReadOnlyDictionary<int, string> RiskyPorts = new Dictionary<int, string>
    {
        [22] = "SSH",
        [23] = "Telnet",
        [53] = "DNS",
        [80] = "HTTP",
        [135] = "Windows RPC",
        [139] = "NetBIOS",
        [443] = "HTTPS",
        [445] = "SMB file sharing",
        [1433] = "SQL Server",
        [3389] = "Remote Desktop",
        [5985] = "WinRM",
        [5986] = "WinRM (HTTPS)",
    };

    public static bool IsValidPort(int port) => port is >= MinPort and <= MaxPort;

    public static PortValidationResult Validate(int gamePort, int queryPort)
    {
        var messages = new List<DiagnosticMessage>();
        var gameValid = IsValidPort(gamePort);
        var queryValid = IsValidPort(queryPort);

        if (!gameValid)
        {
            messages.Add(DiagnosticMessage.Error(
                "GAME_PORT_INVALID",
                "Game port invalid",
                $"The game port must be an integer between {MinPort} and {MaxPort} (got {gamePort}).",
                "Use a port such as 7777."));
        }

        if (!queryValid)
        {
            messages.Add(DiagnosticMessage.Error(
                "QUERY_PORT_INVALID",
                "Query port invalid",
                $"The query port must be an integer between {MinPort} and {MaxPort} (got {queryPort}).",
                "Use a port such as 27015."));
        }

        if (gameValid && queryValid && gamePort == queryPort)
        {
            messages.Add(DiagnosticMessage.Error(
                "PORT_CONFLICT",
                "Game and query port are identical",
                $"The game port and query port must be different (both are {gamePort}). " +
                "Creating firewall rules for an identical pair would be meaningless.",
                "Change one of the two ports on the Server tab."));
        }

        WarnPrivilegedOrRisky(messages, "Game", gamePort, gameValid);
        WarnPrivilegedOrRisky(messages, "Query", queryPort, queryValid);

        var canCreate = gameValid && queryValid && gamePort != queryPort;
        return new PortValidationResult
        {
            CanCreateRules = canCreate,
            Messages = messages,
        };
    }

    private static void WarnPrivilegedOrRisky(
        List<DiagnosticMessage> messages,
        string role,
        int port,
        bool valid)
    {
        if (!valid)
        {
            return;
        }

        if (RiskyPorts.TryGetValue(port, out var service))
        {
            messages.Add(DiagnosticMessage.Warning(
                "PORT_WELL_KNOWN",
                $"{role} port {port} is a well-known service port",
                $"Port {port} is normally used by {service}. Opening it inbound for a game " +
                "server is unsafe and is almost certainly not what you want.",
                "Use the Abiotic Factor defaults (7777 / 27015) or another high port."));
            return;
        }

        if (port <= HighestPrivilegedPort)
        {
            messages.Add(DiagnosticMessage.Warning(
                "PORT_PRIVILEGED",
                $"{role} port {port} is privileged",
                $"Ports {MinPort}-{HighestPrivilegedPort} are reserved system ports. Abiotic " +
                "Factor does not need one and binding it can require extra privileges.",
                "Prefer a high port such as 7777 (game) or 27015 (query)."));
        }
    }
}
