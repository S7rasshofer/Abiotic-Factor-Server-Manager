namespace AbioticServerManager.Core.Runtime;

/// <summary>
/// Honest runtime state. "Process exists" is not "server is online", and a
/// running process can be Blocked by a fatal log condition.
/// </summary>
public enum ServerHealth
{
    Stopped,
    Starting,
    Online,
    Blocked,
    Crashed,
}

/// <summary>
/// Semantic indicator color for a <see cref="ServerHealth"/> value. The actual
/// pixel color lives in the App layer; this enum keeps the mapping in Core so
/// it is unit-testable without pulling in WPF.
/// </summary>
public enum HealthIndicator
{
    Grey,    // Stopped — nothing running
    Yellow,  // Starting — running, not yet ready
    Green,   // Online — ready for players
    Red,     // Blocked / Crashed — running or recently running but unhealthy
}

public static class HealthIndicators
{
    public static HealthIndicator For(ServerHealth health) => health switch
    {
        ServerHealth.Stopped => HealthIndicator.Grey,
        ServerHealth.Starting => HealthIndicator.Yellow,
        ServerHealth.Online => HealthIndicator.Green,
        ServerHealth.Blocked => HealthIndicator.Red,
        ServerHealth.Crashed => HealthIndicator.Red,
        _ => HealthIndicator.Grey,
    };
}

/// <summary>
/// Pure recognition of readiness / blocking log signals. Best-effort token
/// matching - unit-tested so behaviour is pinned even though Abiotic Factor's
/// exact phrasing can shift between builds.
/// </summary>
public static class ServerHealthSignals
{
    private static readonly string[] Readiness =
    [
        "session creation completed",
        "has entered the facility",
        "bringing world",
        "up for play",
        "world loaded",
        "net driver listening",
        "is listening on port",
        "initgame",
    ];

    public static bool IsReadiness(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (var token in Readiness)
        {
            if (text.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A human-readable reason when the line is a blocking signal, else null.</summary>
    public static string? BlockingReason(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        bool Has(string a) => text.Contains(a, StringComparison.OrdinalIgnoreCase);

        if ((Has("corrupt") || Has("corruption")) && Has("world"))
        {
            return "The world save appears to be corrupt.";
        }

        if (Has("address already in use") ||
            Has("failed to bind") || Has("bind failed") ||
            (Has("port") && Has("in use")))
        {
            return "The game or query port could not be bound (already in use).";
        }

        if (Has("sandbox") && (Has("could not find") || Has("invalid") || Has("failed to load")))
        {
            return "The sandbox/admin settings path is invalid.";
        }

        if (Has("session") && (Has("creation failed") || Has("failed to create") ||
                               (Has("EOS") && Has("error"))))
        {
            return "The online session could not be created (EOS/Steam).";
        }

        if (Has("dedicated server will shut down") || Has("fatal error"))
        {
            return "The dedicated server reported a fatal condition and will shut down.";
        }

        return null;
    }
}

public sealed class ServerHealthTracker
{
    public ServerHealth Health { get; private set; } = ServerHealth.Stopped;

    public string Reason { get; private set; } = "Server is stopped.";

    public void OnProcessStarted()
    {
        Health = ServerHealth.Starting;
        Reason = "Server process started; waiting for it to become ready.";
    }

    public void OnProcessExited(bool unexpected)
    {
        if (!unexpected && Health == ServerHealth.Crashed)
        {
            return; // a crash was already detected from the log; keep it
        }

        Health = unexpected ? ServerHealth.Crashed : ServerHealth.Stopped;
        Reason = unexpected
            ? "Server process exited unexpectedly (possible crash)."
            : "Server is stopped.";
    }

    /// <summary>Feeds a log line; returns true if the health state changed.</summary>
    public bool Apply(ServerLogLine line)
    {
        if (Health is ServerHealth.Stopped or ServerHealth.Crashed)
        {
            return false; // not running; ignore stale log noise
        }

        if (line.Text.Contains("exited unexpectedly", StringComparison.OrdinalIgnoreCase))
        {
            Health = ServerHealth.Crashed;
            Reason = "Server process exited unexpectedly (possible crash).";
            return true;
        }

        if (ServerHealthSignals.BlockingReason(line.Text) is { } reason)
        {
            if (Health == ServerHealth.Blocked && Reason == reason)
            {
                return false;
            }

            Health = ServerHealth.Blocked;
            Reason = reason;
            return true;
        }

        if (Health != ServerHealth.Blocked && ServerHealthSignals.IsReadiness(line.Text))
        {
            if (Health == ServerHealth.Online)
            {
                return false;
            }

            Health = ServerHealth.Online;
            Reason = "Server is online and ready for players.";
            return true;
        }

        return false;
    }

    public string StatusText => Health switch
    {
        ServerHealth.Stopped => "Stopped",
        ServerHealth.Starting => "Starting…",
        ServerHealth.Online => "Online",
        ServerHealth.Blocked => "Blocked",
        ServerHealth.Crashed => "Crashed",
        _ => Health.ToString(),
    };
}
