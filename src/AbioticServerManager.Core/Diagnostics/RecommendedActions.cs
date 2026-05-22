using AbioticServerManager.Core.Install;
using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Core.Diagnostics;

/// <summary>
/// One ranked next-step suggestion. <see cref="CommandHint"/> names the
/// observable command the App layer can wire up (e.g.,
/// <c>InstallOrUpdateServerCommand</c>) so the UI doesn't have to interpret
/// free-text — the recommendation is data, not prose.
/// </summary>
public sealed record RecommendedAction
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Detail { get; init; }
    public required RecommendedActionPriority Priority { get; init; }

    /// <summary>Optional name of the App command to invoke. Empty for "informational only".</summary>
    public string CommandHint { get; init; } = "";
}

public enum RecommendedActionPriority
{
    /// <summary>Cosmetic / nice-to-have.</summary>
    Low,

    /// <summary>Will improve confidence or reachability.</summary>
    Medium,

    /// <summary>Required before the server can be reliably started or hosted.</summary>
    High,
}

/// <summary>Inputs the App collects from existing services. Pure value record.</summary>
public sealed record RecommendedActionInputs
{
    public required ServerInstallKind InstallKind { get; init; }
    public required ServerHealth Health { get; init; }
    public required bool FirewallRulesConfigured { get; init; }
    public required bool HasLanIpv4 { get; init; }
    public required bool IsLanOnly { get; init; }
    public required bool WorldsExist { get; init; }
    public required bool HasBlockerFindings { get; init; }
}

/// <summary>
/// Pure ranking of "what should I do next" based on current state. Returns the
/// top three suggestions by priority so the UI panel stays small and actionable.
/// </summary>
public static class RecommendedActions
{
    public const int MaxResults = 3;

    public static IReadOnlyList<RecommendedAction> Build(RecommendedActionInputs inputs)
    {
        var actions = new List<RecommendedAction>();

        // 1. Install state — nothing else matters if there's no server.
        switch (inputs.InstallKind)
        {
            case ServerInstallKind.Missing:
            case ServerInstallKind.EmptyFolder:
                actions.Add(new RecommendedAction
                {
                    Id = "PREPARE_SERVER",
                    Title = "Install the dedicated server",
                    Detail = "Facility Overseer fetches SteamCMD and the Abiotic Factor dedicated server for you.",
                    Priority = RecommendedActionPriority.High,
                    CommandHint = "InstallOrUpdateServerCommand",
                });
                break;
            case ServerInstallKind.InvalidFolder:
                actions.Add(new RecommendedAction
                {
                    Id = "ADOPT_OR_REPAIR_SERVER",
                    Title = "Repair or re-install the dedicated server",
                    Detail = "The selected folder doesn't contain the server. Use Prepare / Update to set up the managed install.",
                    Priority = RecommendedActionPriority.High,
                    CommandHint = "InstallOrUpdateServerCommand",
                });
                break;
            case ServerInstallKind.DetectedUnmanaged:
                actions.Add(new RecommendedAction
                {
                    Id = "VALIDATE_UNMANAGED",
                    Title = "Validate the existing server install",
                    Detail = "An adopted install has no Steam manifest. Running validate confirms files are intact.",
                    Priority = RecommendedActionPriority.Medium,
                    CommandHint = "InstallOrUpdateServerCommand",
                });
                break;
        }

        if (inputs.HasBlockerFindings)
        {
            actions.Add(new RecommendedAction
            {
                Id = "RESOLVE_INTEGRITY_BLOCKERS",
                Title = "Resolve integrity blockers before starting",
                Detail = "World-integrity validation surfaced one or more hard blockers — see the Diagnostics panel.",
                Priority = RecommendedActionPriority.High,
            });
        }

        if (inputs.Health == ServerHealth.Blocked)
        {
            actions.Add(new RecommendedAction
            {
                Id = "HANDLE_BLOCKED",
                Title = "Inspect the Blocked state",
                Detail = "The server reports a fatal condition (e.g., corrupt world or port conflict). Use Create Fresh World or fix the port.",
                Priority = RecommendedActionPriority.High,
            });
        }
        else if (inputs.Health == ServerHealth.Crashed)
        {
            actions.Add(new RecommendedAction
            {
                Id = "RESTART_AFTER_CRASH",
                Title = "Restart the server",
                Detail = "The server exited unexpectedly. Check the log for the cause and try again.",
                Priority = RecommendedActionPriority.High,
                CommandHint = "RestartServerCommand",
            });
        }

        if (inputs.InstallKind is ServerInstallKind.DetectedUnmanaged or ServerInstallKind.SteamCmdManaged)
        {
            if (!inputs.FirewallRulesConfigured && !inputs.IsLanOnly)
            {
                actions.Add(new RecommendedAction
                {
                    Id = "CREATE_FIREWALL_RULES",
                    Title = "Create Windows Firewall rules",
                    Detail = "Required for friends on your LAN or internet to connect. One-click on the Network tab.",
                    Priority = RecommendedActionPriority.Medium,
                    CommandHint = "CreateFirewallRulesCommand",
                });
            }

            if (!inputs.HasLanIpv4)
            {
                actions.Add(new RecommendedAction
                {
                    Id = "CONNECT_TO_NETWORK",
                    Title = "Connect this PC to your network",
                    Detail = "No LAN IPv4 detected — Wi-Fi or Ethernet must be up before anyone can join.",
                    Priority = RecommendedActionPriority.High,
                });
            }
        }

        if (!inputs.WorldsExist)
        {
            actions.Add(new RecommendedAction
            {
                Id = "CREATE_WORLD",
                Title = "Create your first world",
                Detail = "Each world is a tab with its own settings, save, and backups.",
                Priority = RecommendedActionPriority.Medium,
                CommandHint = "CreateWorldCommand",
            });
        }

        if (inputs.Health == ServerHealth.Online && actions.Count == 0)
        {
            actions.Add(new RecommendedAction
            {
                Id = "VERIFY_FRIENDS_CAN_JOIN",
                Title = "Verify a friend can connect",
                Detail = "The server is online. Share your public IP + game port and ask a friend to try joining.",
                Priority = RecommendedActionPriority.Low,
            });
        }

        return actions
            .OrderByDescending(a => (int)a.Priority)
            .Take(MaxResults)
            .ToList();
    }
}
