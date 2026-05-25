namespace AbioticServerManager.Core.Runtime;

/// <summary>Named ordered phase between Start being clicked and the server reporting Online.</summary>
public enum StartupPhase
{
    ProcessStarted,
    NetDriverListening,
    WorldLoading,
    WorldLoaded,
    SessionCreating,
    SessionCreated,
    PlayersCanJoin,
}

public enum StartupPhaseStatus
{
    /// <summary>Not yet observed.</summary>
    Pending,

    /// <summary>Phase has started but is not yet complete.</summary>
    InProgress,

    /// <summary>Observed and completed cleanly.</summary>
    Done,

    /// <summary>Phase was reached but a blocking signal happened during it.</summary>
    Failed,
}

public sealed record StartupPhaseEntry
{
    public required StartupPhase Phase { get; init; }
    public required StartupPhaseStatus Status { get; init; }
    public DateTimeOffset? StartedAt { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }

    /// <summary>Reason / detail line attached to a failed phase, else empty.</summary>
    public string Detail { get; init; } = "";

    public string Label => Phase switch
    {
        StartupPhase.ProcessStarted => "Server process launched",
        StartupPhase.NetDriverListening => "Network driver listening",
        StartupPhase.WorldLoading => "Loading world",
        StartupPhase.WorldLoaded => "World loaded",
        StartupPhase.SessionCreating => "Creating online session",
        StartupPhase.SessionCreated => "Online session created",
        StartupPhase.PlayersCanJoin => "Ready for players",
        _ => Phase.ToString(),
    };
}

public sealed record StartupSequenceSnapshot
{
    public required IReadOnlyList<StartupPhaseEntry> Phases { get; init; }
    public required bool IsRunning { get; init; }

    /// <summary>Total elapsed wall time from process-start to last completed phase (or now).</summary>
    public TimeSpan Elapsed { get; init; }
}

/// <summary>
/// Tracks the startup timeline driven by log signals and server-process events.
/// Pure Core (no IO, no events) - the App layer calls <see cref="OnProcessStarted"/> /
/// <see cref="OnLogLine"/> / <see cref="OnServerStopped"/> and reads the
/// <see cref="Snapshot"/> for binding.
/// </summary>
public sealed class StartupSequenceTracker
{
    private readonly Dictionary<StartupPhase, StartupPhaseEntry> _phases =
        Enum.GetValues<StartupPhase>()
            .ToDictionary(p => p, p => new StartupPhaseEntry
            {
                Phase = p,
                Status = StartupPhaseStatus.Pending,
            });

    private DateTimeOffset? _processStartedAt;
    private DateTimeOffset _now = DateTimeOffset.UtcNow;
    private bool _running;

    /// <summary>Override the clock for tests; defaults to <see cref="DateTimeOffset.UtcNow"/>.</summary>
    public Func<DateTimeOffset> Clock { get; set; } = () => DateTimeOffset.UtcNow;

    public StartupSequenceSnapshot Snapshot
    {
        get
        {
            var ordered = Enum.GetValues<StartupPhase>().Select(p => _phases[p]).ToList();
            var lastTime =
                ordered.Where(p => p.CompletedAt is not null).Max(p => p.CompletedAt) ??
                _processStartedAt ?? Clock();
            var elapsed = _processStartedAt is null
                ? TimeSpan.Zero
                : lastTime - _processStartedAt.Value;

            return new StartupSequenceSnapshot
            {
                Phases = ordered,
                IsRunning = _running,
                Elapsed = elapsed,
            };
        }
    }

    public void Reset()
    {
        foreach (var p in Enum.GetValues<StartupPhase>())
        {
            _phases[p] = new StartupPhaseEntry { Phase = p, Status = StartupPhaseStatus.Pending };
        }

        _processStartedAt = null;
        _running = false;
    }

    public void OnProcessStarted()
    {
        Reset();
        _running = true;
        _processStartedAt = Clock();
        SetDone(StartupPhase.ProcessStarted);
        SetInProgress(StartupPhase.NetDriverListening);
    }

    public void OnServerStopped(bool unexpected)
    {
        _running = false;
        if (!unexpected)
        {
            return;
        }

        // Mark the still-pending or in-progress phase as failed so the timeline
        // shows where the crash happened.
        foreach (var phase in Enum.GetValues<StartupPhase>())
        {
            if (_phases[phase].Status is StartupPhaseStatus.InProgress or StartupPhaseStatus.Pending)
            {
                _phases[phase] = _phases[phase] with
                {
                    Status = StartupPhaseStatus.Failed,
                    CompletedAt = Clock(),
                    Detail = "Server exited unexpectedly before this phase completed.",
                };
                break;
            }
        }
    }

    /// <summary>
    /// Direct evidence (EOS lobby code, A2S reply) confirmed the server is
    /// genuinely online. That is ground truth: if the server is now accepting
    /// players, every prior startup phase MUST have succeeded - any earlier
    /// "Failed" mark came from a transient false-positive blocking signal that
    /// the server then recovered from on its own. Promote every phase to Done
    /// so the timeline and the "Startup failed" summary stop contradicting the
    /// live Online state.
    /// </summary>
    public bool OnConfirmedOnline()
    {
        var changed = false;
        var now = Clock();
        foreach (var phase in Enum.GetValues<StartupPhase>())
        {
            if (_phases[phase].Status == StartupPhaseStatus.Done)
            {
                continue;
            }

            _phases[phase] = _phases[phase] with
            {
                Status = StartupPhaseStatus.Done,
                StartedAt = _phases[phase].StartedAt ?? now,
                CompletedAt = now,
                Detail = "", // clear stale failure reason so the UI does not lie.
            };
            changed = true;
        }

        return changed;
    }

    /// <summary>
    /// Feeds a log line and advances the timeline if it matches a known signal.
    /// Returns true when the snapshot changed (so the App can raise PropertyChanged).
    /// </summary>
    public bool OnLogLine(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var changed = false;

        bool Has(string a) => text.Contains(a, StringComparison.OrdinalIgnoreCase);

        if (Has("net driver listening") || Has("is listening on port"))
        {
            changed |= AdvanceTo(StartupPhase.NetDriverListening, completeCurrent: true);
            changed |= SetInProgress(StartupPhase.WorldLoading);
        }

        if (Has("bringing world") || Has("loading world"))
        {
            changed |= SetInProgress(StartupPhase.WorldLoading);
        }

        if (Has("world loaded") || Has("up for play") || Has("initgame"))
        {
            changed |= AdvanceTo(StartupPhase.WorldLoaded, completeCurrent: true);
            changed |= SetInProgress(StartupPhase.SessionCreating);
        }

        if (Has("creating session") || Has("session create"))
        {
            changed |= SetInProgress(StartupPhase.SessionCreating);
        }

        if (Has("session creation completed") || Has("session created"))
        {
            changed |= AdvanceTo(StartupPhase.SessionCreated, completeCurrent: true);
            changed |= SetInProgress(StartupPhase.PlayersCanJoin);
        }

        if (Has("has entered the facility") ||
            (Has("server is online") || Has("ready for players")))
        {
            changed |= AdvanceTo(StartupPhase.PlayersCanJoin, completeCurrent: true);
        }

        if (ServerHealthSignals.BlockingReason(text) is { } reason)
        {
            // Mark the currently in-progress phase as failed with the reason.
            foreach (var phase in Enum.GetValues<StartupPhase>())
            {
                if (_phases[phase].Status == StartupPhaseStatus.InProgress)
                {
                    _phases[phase] = _phases[phase] with
                    {
                        Status = StartupPhaseStatus.Failed,
                        CompletedAt = Clock(),
                        Detail = reason,
                    };
                    changed = true;
                    break;
                }
            }
        }

        return changed;
    }

    private bool AdvanceTo(StartupPhase phase, bool completeCurrent)
    {
        var changed = false;
        if (completeCurrent)
        {
            foreach (var p in Enum.GetValues<StartupPhase>())
            {
                if (p == phase) break;
                if (_phases[p].Status == StartupPhaseStatus.InProgress)
                {
                    _phases[p] = _phases[p] with
                    {
                        Status = StartupPhaseStatus.Done,
                        CompletedAt = Clock(),
                    };
                    changed = true;
                }
            }
        }

        changed |= SetDone(phase);
        return changed;
    }

    private bool SetInProgress(StartupPhase phase)
    {
        if (_phases[phase].Status is StartupPhaseStatus.Done or StartupPhaseStatus.Failed)
        {
            return false;
        }

        if (_phases[phase].Status == StartupPhaseStatus.InProgress)
        {
            return false;
        }

        _phases[phase] = _phases[phase] with
        {
            Status = StartupPhaseStatus.InProgress,
            StartedAt = _phases[phase].StartedAt ?? Clock(),
        };
        return true;
    }

    private bool SetDone(StartupPhase phase)
    {
        if (_phases[phase].Status == StartupPhaseStatus.Done)
        {
            return false;
        }

        _phases[phase] = _phases[phase] with
        {
            Status = StartupPhaseStatus.Done,
            StartedAt = _phases[phase].StartedAt ?? Clock(),
            CompletedAt = Clock(),
        };
        return true;
    }
}
