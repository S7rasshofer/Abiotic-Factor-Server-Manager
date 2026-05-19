using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Config;

public sealed class ConfigValidator : IConfigValidator
{
    public const int MinPlayers = 1;
    public const int MaxPlayers = 24;
    public const int RecommendedMaxPlayers = 6;

    public IReadOnlyList<DiagnosticMessage> Validate(
        ServerInstance instance,
        IReadOnlyList<ServerInstance> otherInstances)
    {
        var results = new List<DiagnosticMessage>();

        if (string.IsNullOrWhiteSpace(instance.SteamServerName))
        {
            results.Add(DiagnosticMessage.Error(
                "SERVER_NAME_EMPTY",
                "Server name is blank",
                "The server name must be set or the server will not list cleanly.",
                "Enter a Server Name on the Server tab."));
        }

        if (string.IsNullOrWhiteSpace(instance.WorldSaveName))
        {
            results.Add(DiagnosticMessage.Error(
                "WORLD_NAME_EMPTY",
                "Server save name is blank",
                "The server name also identifies the save folder and cannot be empty.",
                "Enter a Server Name on the Server tab."));
        }

        if (instance.MaxPlayers < MinPlayers || instance.MaxPlayers > MaxPlayers)
        {
            results.Add(DiagnosticMessage.Error(
                "MAX_PLAYERS_RANGE",
                "Max players out of range",
                $"Max players must be between {MinPlayers} and {MaxPlayers}.",
                $"Set Max Players to a value from {MinPlayers} to {MaxPlayers}."));
        }
        else if (instance.MaxPlayers > RecommendedMaxPlayers)
        {
            results.Add(DiagnosticMessage.Warning(
                "MAX_PLAYERS_RECOMMENDED",
                "Max players above recommended",
                $"More than {RecommendedMaxPlayers} players can stress the server and host machine.",
                "Lower Max Players if you see performance issues."));
        }

        if (!IsValidPort(instance.GamePort))
        {
            results.Add(DiagnosticMessage.Error(
                "GAME_PORT_INVALID",
                "Game port invalid",
                "The game port must be between 1 and 65535.",
                "Use a port such as 7777."));
        }

        if (!IsValidPort(instance.QueryPort))
        {
            results.Add(DiagnosticMessage.Error(
                "QUERY_PORT_INVALID",
                "Query port invalid",
                "The query port must be between 1 and 65535.",
                "Use a port such as 27015."));
        }

        if (IsValidPort(instance.GamePort) &&
            instance.GamePort == instance.QueryPort)
        {
            results.Add(DiagnosticMessage.Error(
                "PORT_CONFLICT",
                "Game and query port are identical",
                "The game port and query port must be different.",
                "Change one of the two ports."));
        }

        foreach (var other in otherInstances)
        {
            if (other.Id == instance.Id)
            {
                continue;
            }

            if (PortsOverlap(instance, other))
            {
                results.Add(DiagnosticMessage.Error(
                    "PORT_CONFLICT",
                    "Port conflict with another world",
                    $"World \"{other.DisplayName}\" already uses one of these ports.",
                    "Give each world a unique game port and query port."));
                break;
            }
        }

        if (string.IsNullOrEmpty(instance.ServerPassword))
        {
            results.Add(DiagnosticMessage.Info(
                "PASSWORD_EMPTY",
                "No server password",
                "Anyone who can reach the server will be able to join."));
        }

        if (string.IsNullOrEmpty(instance.AdminPassword))
        {
            results.Add(DiagnosticMessage.Warning(
                "ADMIN_PASSWORD_EMPTY",
                "No admin password",
                "Without an admin password you cannot use in-game admin commands.",
                "Set an Admin Password on the Server tab."));
        }

        if (!string.IsNullOrWhiteSpace(instance.SandboxIniPath) &&
            !File.Exists(instance.SandboxIniPath))
        {
            results.Add(DiagnosticMessage.Warning(
                "SANDBOX_PATH_MISSING",
                "Sandbox settings file missing",
                $"SandboxSettings.ini was not found at {instance.SandboxIniPath}.",
                "It will be created when the server first generates default files."));
        }

        if (!string.IsNullOrWhiteSpace(instance.WorldPath) &&
            !Directory.Exists(instance.WorldPath))
        {
            results.Add(DiagnosticMessage.Warning(
                "WORLD_PATH_MISSING",
                "World folder missing",
                $"The world folder was not found at {instance.WorldPath}.",
                "It will be created on first launch or when importing a save."));
        }

        if (instance.UseLocalIps)
        {
            results.Add(DiagnosticMessage.Warning(
                "USE_LOCAL_IPS_WARNING",
                "Use Local IPs is enabled",
                "Local IP binding can help LAN clients but may break joining over the internet.",
                "Disable Use Local IPs unless you specifically need LAN-only behaviour."));
        }

        if (instance.LanOnly)
        {
            results.Add(DiagnosticMessage.Info(
                "LAN_ONLY_VISIBILITY",
                "LAN-only mode is on",
                "The server is intentionally not expected to appear on the public internet."));
        }

        if (results.All(r => r.Severity is DiagnosticSeverity.Info or DiagnosticSeverity.Success))
        {
            results.Insert(0, DiagnosticMessage.Success(
                "CONFIG_OK",
                "Configuration looks valid",
                "No blocking configuration problems were found."));
        }

        return results;
    }

    private static bool IsValidPort(int port) => port is > 0 and <= 65535;

    private static bool PortsOverlap(ServerInstance a, ServerInstance b) =>
        a.GamePort == b.GamePort ||
        a.QueryPort == b.QueryPort ||
        a.GamePort == b.QueryPort ||
        a.QueryPort == b.GamePort;
}
