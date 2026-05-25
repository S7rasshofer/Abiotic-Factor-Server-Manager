namespace AbioticServerManager.Core.Diagnostics;

/// <summary>
/// Single top-level "can players actually reach this server" rollup. The
/// Network tab's granular check list is honest about every detail; users need
/// one pinned verdict that says yes / not yet / no. Pure record so the rules
/// are unit-testable without WPF.
/// </summary>
public enum NetworkVerdictStatus
{
    /// <summary>No server is running, so reachability is not applicable.</summary>
    Stopped,

    /// <summary>Server is up and answering Steam queries - players can join.</summary>
    Reachable,

    /// <summary>Server is bound to its ports but has not answered a query yet.</summary>
    BindingOrWarming,

    /// <summary>Server is running but its ports are not bound. Something is wrong.</summary>
    Unreachable,
}

/// <summary>
/// Inputs the App collects to compute the verdict. All value-typed so the
/// scoring stays pure - no IO, no WPF.
/// </summary>
public sealed record NetworkVerdictInputs
{
    public required bool ServerProcessRunning { get; init; }

    /// <summary>True when the local A2S query against 127.0.0.1:QueryPort got an InfoReply.</summary>
    public required bool A2SLocalResponded { get; init; }

    /// <summary>True when the dedicated server has bound the game UDP port.</summary>
    public required bool GamePortBound { get; init; }

    /// <summary>True when the dedicated server has bound the query UDP port.</summary>
    public required bool QueryPortBound { get; init; }

    /// <summary>True when the server published an EOS lobby code (session is live).</summary>
    public required bool LobbyCodePublished { get; init; }

    /// <summary>True for a world configured to host only on the LAN.</summary>
    public required bool IsLanOnly { get; init; }
}

public sealed record NetworkVerdict
{
    public required NetworkVerdictStatus Status { get; init; }
    public required string Headline { get; init; }
    public required string Detail { get; init; }
}

public static class NetworkVerdictRules
{
    public static NetworkVerdict Evaluate(NetworkVerdictInputs inputs)
    {
        if (!inputs.ServerProcessRunning)
        {
            return new NetworkVerdict
            {
                Status = NetworkVerdictStatus.Stopped,
                Headline = "Server is stopped.",
                Detail = "Start the world to check reachability.",
            };
        }

        // A2S reply OR a published lobby code is direct proof a player can join.
        // A2S is the canonical Steam query protocol; an EOS lobby code means the
        // online session is up. Either is sufficient evidence for a green verdict.
        if (inputs.A2SLocalResponded || inputs.LobbyCodePublished)
        {
            var headline = inputs.IsLanOnly
                ? "Players on this LAN can join."
                : "Server is reachable - players can join.";
            var detail = inputs.A2SLocalResponded && inputs.LobbyCodePublished
                ? "Local Steam query replied and an EOS lobby code is live."
                : inputs.A2SLocalResponded
                    ? "The dedicated server answered a local Steam (A2S) query."
                    : "The server published an EOS lobby code; the online session is live.";

            return new NetworkVerdict
            {
                Status = NetworkVerdictStatus.Reachable,
                Headline = headline,
                Detail = detail,
            };
        }

        if (inputs.GamePortBound && inputs.QueryPortBound)
        {
            return new NetworkVerdict
            {
                Status = NetworkVerdictStatus.BindingOrWarming,
                Headline = "Server is bound; warming up...",
                Detail = "Both UDP ports are bound. Waiting for the first Steam query reply.",
            };
        }

        var missing = (inputs.GamePortBound, inputs.QueryPortBound) switch
        {
            (false, false) => "Neither the game UDP port nor the query UDP port is bound.",
            (false, true) => "The game UDP port is not bound.",
            (true, false) => "The query UDP port is not bound.",
            _ => "A UDP port is not bound.",
        };

        return new NetworkVerdict
        {
            Status = NetworkVerdictStatus.Unreachable,
            Headline = "Server is running but unreachable.",
            Detail = missing + " Check the log for bind errors and ensure no other process holds the port.",
        };
    }
}
