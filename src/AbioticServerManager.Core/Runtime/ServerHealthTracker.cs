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
    Grey,    // Stopped - nothing running
    Yellow,  // Starting - running, not yet ready
    Green,   // Online - ready for players
    Red,     // Blocked / Crashed - running or recently running but unhealthy
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

        if (IsCorruptWorldSignal(text))
        {
            return "The world save appears to be corrupt.";
        }

        if (IsPortBindFailure(text))
        {
            return "The game or query port could not be bound (already in use).";
        }

        if (Has("sandbox") && (Has("could not find") || Has("invalid") || Has("failed to load")))
        {
            return "The sandbox/admin settings path is invalid.";
        }

        if (IsSessionCreationFailure(text))
        {
            return "The online session could not be created (EOS/Steam).";
        }

        if (Has("dedicated server will shut down") || IsFatalShutdownSignal(text))
        {
            return "The dedicated server reported a fatal condition and will shut down.";
        }

        return null;
    }

    /// <summary>
    /// True only for log lines that genuinely express a bind failure. The
    /// previous rule false-positived on benign lines that happened to contain
    /// "port" and "in use" anywhere on the same line (e.g. routine "query port
    /// 27015 in use by AbioticFactorServer" status logs). Match requires either
    /// an explicit bind/listen failure verb or the canonical EADDRINUSE phrase
    /// alongside a bind/socket context word.
    /// </summary>
    private static bool IsPortBindFailure(string text)
    {
        bool Has(string a) => text.Contains(a, StringComparison.OrdinalIgnoreCase);

        if (Has("failed to bind") || Has("bind failed") || Has("could not bind"))
        {
            return true;
        }

        if (Has("address already in use") &&
            (Has("bind") || Has("listen") || Has("socket")))
        {
            return true;
        }

        // EADDRINUSE / WSAEADDRINUSE error codes appear on real OS-level failures.
        return Has("eaddrinuse") || Has("wsaeaddrinuse");
    }

    /// <summary>
    /// True only for fatal EOS/Steam session-creation failures. The previous
    /// `Has("session") && Has("EOS") && Has("error")` rule false-positived on
    /// transient EOS verbose/warning lines that mention "session" and "error"
    /// during normal startup retries.
    /// </summary>
    private static bool IsSessionCreationFailure(string text)
    {
        bool Has(string a) => text.Contains(a, StringComparison.OrdinalIgnoreCase);

        if (Has("session") && (Has("creation failed") || Has("failed to create")))
        {
            return true;
        }

        // Narrow EOS failure phrasings: the SDK names the failing call.
        if (Has("eos_session_create") && (Has("error") || Has("failed")))
        {
            return true;
        }

        if (Has("eos") && Has("session") &&
            (Has("not created") || Has("creation aborted") || Has("aborted creation")))
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// True when the line is a real UE5 fatal-error marker, not a benign log
    /// line that happens to contain the words "fatal" and "error" (e.g.
    /// "Fatal error category: none" type diagnostics).
    /// </summary>
    private static bool IsFatalShutdownSignal(string text)
    {
        bool Has(string a) => text.Contains(a, StringComparison.OrdinalIgnoreCase);

        // UE5 canonical fatal markers.
        if (Has("logwindows: error: === critical error") ||
            Has("=== critical error: ===") ||
            Has("assertion failed:"))
        {
            return true;
        }

        // "Fatal error" only when paired with an unambiguous failure / abort verb.
        return Has("fatal error") &&
               (Has("encountered") || Has("aborting") || Has("shutting down") ||
                Has("will shut down") || Has("crash"));
    }

    /// <summary>
    /// Sec 4.2: a stable recovery-flow trigger tag when the blocking signal has a
    /// guided recovery flow (see <c>RecoveryFlows</c>), else null. Parallels
    /// <see cref="BlockingReason"/> - only corruption and port-bind failures
    /// have a guided flow; other blocking reasons return null.
    /// </summary>
    public static string? BlockingTag(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (IsCorruptWorldSignal(text))
        {
            return "world.corrupt";
        }

        if (IsPortBindFailure(text))
        {
            return "port.bind_fail";
        }

        return null;
    }

    /// <summary>
    /// True when a log line genuinely reports the world save as corrupt, not a
    /// benign integrity-check probe. The previous rule (corrupt+world anywhere
    /// on the line) false-positived on routine startup lines like
    /// "world loaded - no corruption detected", flipping a healthy fresh world
    /// into Blocked. Match requires the failure to be expressed: a contiguous
    /// "X is corrupt" / "corrupt X" phrase, or the keyword paired with an
    /// unambiguous failure verb on the same line. The ambiguous noun phrases
    /// "save corruption" / "world corruption" deliberately do NOT match alone -
    /// they appear in benign probes ("no save corruption detected") - and only
    /// trigger when accompanied by a failure verb on the same line.
    /// </summary>
    private static bool IsCorruptWorldSignal(string text)
    {
        bool Has(string a) => text.Contains(a, StringComparison.OrdinalIgnoreCase);

        if (!(Has("corrupt") || Has("corruption")))
        {
            return false;
        }

        if (Has("save is corrupt") || Has("world is corrupt") ||
            Has("corrupt save") || Has("corrupt world") ||
            Has("appears to be corrupt"))
        {
            return true;
        }

        return Has("cannot be loaded") || Has("could not be loaded") ||
               Has("failed to load") || Has("unable to load") ||
               Has("load failed") || Has("loading failed") ||
               Has("aborted") || Has("fatal");
    }
}

public sealed class ServerHealthTracker
{
    public ServerHealth Health { get; private set; } = ServerHealth.Stopped;

    public string Reason { get; private set; } = "Server is stopped.";

    /// <summary>
    /// Sec 4.2: recovery-flow trigger tag set while <see cref="Health"/> is
    /// <see cref="ServerHealth.Blocked"/> by a signal that has a guided flow
    /// (<c>world.corrupt</c> / <c>port.bind_fail</c>), else null.
    /// </summary>
    public string? BlockingTag { get; private set; }

    public void OnProcessStarted()
    {
        Health = ServerHealth.Starting;
        Reason = "Server process started; waiting for it to become ready.";
        BlockingTag = null;
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
            BlockingTag = ServerHealthSignals.BlockingTag(line.Text);
            return true;
        }

        // Readiness signals also recover the world from Blocked: a transient
        // blocking line that the server then resolved on its own should not
        // pin the UI to red forever. The previous one-way trap turned a
        // single false-positive into a permanent "broken" indication even
        // when the server published its EOS lobby code and started accepting
        // players. Real fatal conditions never emit later readiness signals,
        // so this is safe.
        if (ServerHealthSignals.IsReadiness(line.Text))
        {
            if (Health == ServerHealth.Online)
            {
                return false;
            }

            Health = ServerHealth.Online;
            Reason = "Server is online and ready for players.";
            BlockingTag = null;
            return true;
        }

        return false;
    }

    /// <summary>
    /// External "the server is actually responding" proof - e.g. an A2S reply
    /// from the local query port, or the EOS session publishing a lobby code.
    /// Either is direct evidence the server is online regardless of what the
    /// log heuristics decided. Promotes Starting / Blocked to Online and
    /// clears the blocking tag. Returns true when the state changed.
    /// </summary>
    public bool ConfirmOnlineFromCorroboration(string reason)
    {
        if (Health is ServerHealth.Stopped or ServerHealth.Crashed or ServerHealth.Online)
        {
            return false;
        }

        Health = ServerHealth.Online;
        Reason = string.IsNullOrWhiteSpace(reason)
            ? "Server is online and ready for players."
            : reason;
        BlockingTag = null;
        return true;
    }

    public string StatusText => Health switch
    {
        ServerHealth.Stopped => "Stopped",
        ServerHealth.Starting => "Starting...",
        ServerHealth.Online => "Online",
        ServerHealth.Blocked => "Blocked",
        ServerHealth.Crashed => "Crashed",
        _ => Health.ToString(),
    };
}
