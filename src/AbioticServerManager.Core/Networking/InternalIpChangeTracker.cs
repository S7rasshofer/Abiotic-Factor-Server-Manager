namespace AbioticServerManager.Core.Networking;

/// <summary>
/// Result of comparing the current LAN IPv4 against the previously persisted
/// snapshot. A change is worth surfacing because the user may have port
/// forwarding rules pinned to the old IP — DHCP renegotiations and adapter
/// reshuffles silently break "friends can join over the internet".
/// </summary>
public enum InternalIpChange
{
    /// <summary>Never recorded a snapshot before. Nothing to compare.</summary>
    FirstRun,

    /// <summary>Current IPv4 matches the last persisted snapshot.</summary>
    Unchanged,

    /// <summary>Current IPv4 differs from the last persisted snapshot.</summary>
    Changed,

    /// <summary>We previously had an IPv4 but currently have none (disconnected, VPN flap).</summary>
    Lost,
}

/// <summary>
/// Small durable record of the host's LAN IPv4 at a given moment. Stored under
/// <c>&lt;DataRoot&gt;/config/last-internal-ip.json</c> so a change between
/// launches can be detected and a "port forwarding may need attention" banner
/// surfaced.
/// </summary>
public sealed record InternalIpSnapshot(string Ipv4, DateTimeOffset CapturedAt);

public static class InternalIpChangeTracker
{
    /// <summary>
    /// Pure comparison so the policy is unit-testable without filesystem or
    /// network access.
    /// </summary>
    public static InternalIpChange Detect(InternalIpSnapshot? lastSeen, string? currentIpv4)
    {
        if (lastSeen is null)
        {
            return InternalIpChange.FirstRun;
        }

        if (string.IsNullOrWhiteSpace(currentIpv4))
        {
            return InternalIpChange.Lost;
        }

        return string.Equals(lastSeen.Ipv4, currentIpv4, StringComparison.Ordinal)
            ? InternalIpChange.Unchanged
            : InternalIpChange.Changed;
    }

    /// <summary>
    /// Builds the snapshot to persist for the next launch. Returns null when
    /// we have nothing useful to record (so we do not clobber a good last-seen
    /// value with a transient nothing).
    /// </summary>
    public static InternalIpSnapshot? SnapshotFor(string? currentIpv4, DateTimeOffset now) =>
        string.IsNullOrWhiteSpace(currentIpv4) ? null : new InternalIpSnapshot(currentIpv4, now);
}
