using AbioticServerManager.Core.Models;

namespace AbioticServerManager.Core.Diagnostics;

public enum NetworkCheckStatus
{
    Pass,
    Warn,
    Fail,
    Unknown,
    NeedsAdmin,
}

public sealed record NetworkCheckResult
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required NetworkCheckStatus Status { get; init; }
    public required string Summary { get; init; }
    public string Details { get; init; } = "";
    public string Remediation { get; init; } = "";
    public string Category { get; init; } = "";
    public string Value { get; init; } = "";

    public string StatusLabel => Status switch
    {
        NetworkCheckStatus.Pass => "Pass",
        NetworkCheckStatus.Warn => "Warn",
        NetworkCheckStatus.Fail => "Fail",
        NetworkCheckStatus.NeedsAdmin => "Needs admin",
        _ => "Unknown",
    };
}

/// <summary>
/// One row in the Network tab's setup report. Covers both the actual firewall
/// rules and the environment/process/port diagnostics, distinguished by
/// <see cref="Category"/> so the "are the rules configured" rollup only looks at
/// real rule rows.
/// </summary>
public sealed record FirewallSetupCheck
{
    public required string Purpose { get; init; }
    public required string Kind { get; init; }
    public required string Value { get; init; }
    public required bool IsConfigured { get; init; }
    public required string StatusText { get; init; }
    public string Detail { get; init; } = "";

    /// <summary>
    /// <c>FirewallRule</c> for the three rules that must exist; <c>Environment</c>
    /// for elevation/profile; <c>Port</c> for UDP binding/process ownership.
    /// </summary>
    public string Category { get; init; } = SetupCheckCategory.FirewallRule;

    /// <summary>True only when this check is a hard requirement that is unmet.</summary>
    public bool IsBlocking { get; init; }
}

public static class SetupCheckCategory
{
    public const string FirewallRule = "FirewallRule";
    public const string Environment = "Environment";
    public const string Port = "Port";
}

public enum FirewallRuleRole
{
    Game,
    Query,
    Program,
}

/// <summary>Result of inspecting one managed Facility Overseer rule.</summary>
public sealed record FirewallRuleStatus
{
    public required FirewallRuleRole Role { get; init; }
    public required bool Exists { get; init; }

    /// <summary>Exists AND protocol/port/profile/direction/action/enabled all correct.</summary>
    public required bool IsCorrect { get; init; }
    public string DisplayName { get; init; } = "";
    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>Local UDP binding + owning process for one port.</summary>
public sealed record PortBindingStatus
{
    public required int Port { get; init; }
    public required string Role { get; init; }
    public required bool IsListening { get; init; }
    public IReadOnlyList<int> OwningPids { get; init; } = [];
    public IReadOnlyList<string> OwningProcesses { get; init; } = [];
    public IReadOnlyList<string> OwningProcessPaths { get; init; } = [];

    /// <summary>True when something other than the dedicated server holds the port.</summary>
    public bool ForeignOwner { get; init; }
}

/// <summary>Host facts that change how the result should be read.</summary>
public sealed record NetworkEnvironment
{
    public required bool IsElevated { get; init; }

    /// <summary>Active connection profile category: Public / Private / Domain / Unknown.</summary>
    public string NetworkProfile { get; init; } = "Unknown";

    public bool ServerProcessRunning { get; init; }
    public IReadOnlyList<string> ServerProcessNames { get; init; } = [];
}

/// <summary>Structured outcome the elevated rule-writer reports back via a temp file.</summary>
public sealed record FirewallApplyOutcome
{
    public required bool Success { get; init; }
    public int RulesCreated { get; init; }
    public int RulesUpdated { get; init; }
    public int StaleRulesRemoved { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public string RawLog { get; init; } = "";
}

public sealed record NetworkSetupStatus
{
    public required IReadOnlyList<NetworkCheckResult> Checks { get; init; }
    public required IReadOnlyList<string> RouterChecklist { get; init; }
    public required IReadOnlyList<string> LocalIpv4Addresses { get; init; }
    public IReadOnlyList<string> LocalIpv4CandidateDetails { get; init; } = [];
    public string? SuggestedRouterTarget { get; init; }
    public string? ServerExecutablePath { get; init; }
    public string? InspectionError { get; init; }
    public string? LogPath { get; init; }

    public IReadOnlyList<DiagnosticMessage> PortValidationMessages { get; init; } = [];
    public NetworkEnvironment? Environment { get; init; }
    public IReadOnlyList<PortBindingStatus> PortBindings { get; init; } = [];
    public string? LastRouterChecklistIpv4 { get; init; }
    public DateTimeOffset? LastRouterChecklistCopiedAtUtc { get; init; }
    public DateTimeOffset? LastFirewallRepairAtUtc { get; init; }

    /// <summary>Set when more than one plausible LAN IPv4 was detected.</summary>
    public string? MultipleIpWarning { get; init; }

    /// <summary>True only when all three managed rules exist and are correct.</summary>
    public bool AreFirewallRulesConfigured =>
        Checks.Count > 0 &&
        Checks
            .Where(c => c.Category == SetupCheckCategory.FirewallRule)
            .All(c => c.Status == NetworkCheckStatus.Pass);
}

public sealed record FirewallSetupResult
{
    public required bool Success { get; init; }
    public required string Message { get; init; }

    /// <summary>Full stdout/stderr of the elevated run, for the diagnostics panel/log.</summary>
    public string Diagnostics { get; init; } = "";

    /// <summary>True when the only problem was the user declining the UAC prompt.</summary>
    public bool NeedsAdmin { get; init; }

    public string? LogPath { get; init; }
}

public interface INetworkSetupService
{
    Task<NetworkSetupStatus> InspectAsync(ServerInstance instance, CancellationToken ct = default);

    Task<FirewallSetupResult> EnsureFirewallRulesAsync(
        ServerInstance instance,
        CancellationToken ct = default);
}
