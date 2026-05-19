using AbioticServerManager.Core.Diagnostics;

namespace AbioticServerManager.Core.Runtime;

/// <summary>
/// Live, non-persisted status for a single server instance. The plan deliberately splits
/// status into independent signals: a single green dot is not enough to tell a user whether
/// their server is actually joinable.
/// </summary>
public sealed class ServerRuntimeState
{
    public bool IsProcessRunning { get; set; }
    public bool IsConfigValid { get; set; }
    public bool IsLocalQueryResponding { get; set; }
    public bool IsExternallyVisible { get; set; }
    public bool IsVersionLikelyCurrent { get; set; }

    public int? ProcessId { get; set; }
    public DateTimeOffset? StartedAt { get; set; }

    public string StatusMessage { get; set; } = "Stopped";
    public List<DiagnosticMessage> Diagnostics { get; set; } = [];
}
