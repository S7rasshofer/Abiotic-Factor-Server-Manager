using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.RuntimeTests;

public class StartupSequenceTrackerTests
{
    private static StartupSequenceTracker NewTracker()
    {
        var now = new DateTimeOffset(2026, 5, 20, 12, 0, 0, TimeSpan.Zero);
        var tracker = new StartupSequenceTracker();
        tracker.Clock = () => now;
        return tracker;
    }

    [Fact]
    public void Fresh_tracker_has_all_phases_pending()
    {
        var snap = new StartupSequenceTracker().Snapshot;
        Assert.All(snap.Phases, p => Assert.Equal(StartupPhaseStatus.Pending, p.Status));
        Assert.False(snap.IsRunning);
    }

    [Fact]
    public void Process_started_marks_first_two_phases_active()
    {
        var t = NewTracker();
        t.OnProcessStarted();

        var snap = t.Snapshot;
        Assert.True(snap.IsRunning);
        var process = snap.Phases.Single(p => p.Phase == StartupPhase.ProcessStarted);
        var netDriver = snap.Phases.Single(p => p.Phase == StartupPhase.NetDriverListening);
        Assert.Equal(StartupPhaseStatus.Done, process.Status);
        Assert.Equal(StartupPhaseStatus.InProgress, netDriver.Status);
    }

    [Fact]
    public void Net_driver_listening_advances_to_world_loading()
    {
        var t = NewTracker();
        t.OnProcessStarted();

        var changed = t.OnLogLine("LogNet: Net driver listening on 0.0.0.0:7777");

        Assert.True(changed);
        var snap = t.Snapshot;
        Assert.Equal(StartupPhaseStatus.Done,
            snap.Phases.Single(p => p.Phase == StartupPhase.NetDriverListening).Status);
        Assert.Equal(StartupPhaseStatus.InProgress,
            snap.Phases.Single(p => p.Phase == StartupPhase.WorldLoading).Status);
    }

    [Fact]
    public void Happy_path_drives_every_phase_to_done()
    {
        var t = NewTracker();
        t.OnProcessStarted();
        t.OnLogLine("net driver listening");
        t.OnLogLine("Bringing world Cascade up for play");
        t.OnLogLine("World loaded");
        t.OnLogLine("Session creation completed");
        t.OnLogLine("CHAT LOG:  S7razzy has entered the facility.");

        var snap = t.Snapshot;
        Assert.All(snap.Phases, p =>
            Assert.True(p.Status == StartupPhaseStatus.Done,
                $"Phase {p.Phase} expected Done, was {p.Status}"));
    }

    [Fact]
    public void Blocking_log_signal_marks_active_phase_failed_with_reason()
    {
        var t = NewTracker();
        t.OnProcessStarted();
        t.OnLogLine("net driver listening");      // → WorldLoading in-progress
        var changed = t.OnLogLine("World save appears to be corrupt");

        Assert.True(changed);
        var loading = t.Snapshot.Phases.Single(p => p.Phase == StartupPhase.WorldLoading);
        Assert.Equal(StartupPhaseStatus.Failed, loading.Status);
        Assert.Contains("corrupt", loading.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Unexpected_stop_marks_pending_phase_failed()
    {
        var t = NewTracker();
        t.OnProcessStarted();
        t.OnServerStopped(unexpected: true);

        // The first in-progress (NetDriverListening) should be marked failed.
        var netDriver = t.Snapshot.Phases.Single(p => p.Phase == StartupPhase.NetDriverListening);
        Assert.Equal(StartupPhaseStatus.Failed, netDriver.Status);
        Assert.Contains("unexpectedly", netDriver.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Graceful_stop_does_not_mark_phases_failed()
    {
        var t = NewTracker();
        t.OnProcessStarted();
        t.OnLogLine("net driver listening");
        t.OnServerStopped(unexpected: false);

        // Active phases stay as-is (Done/InProgress) — graceful stop is not a failure.
        Assert.DoesNotContain(t.Snapshot.Phases, p => p.Status == StartupPhaseStatus.Failed);
        Assert.False(t.Snapshot.IsRunning);
    }

    [Fact]
    public void Reset_clears_all_phases_back_to_pending()
    {
        var t = NewTracker();
        t.OnProcessStarted();
        t.OnLogLine("net driver listening");
        t.OnLogLine("World loaded");

        t.Reset();

        Assert.All(t.Snapshot.Phases, p => Assert.Equal(StartupPhaseStatus.Pending, p.Status));
        Assert.False(t.Snapshot.IsRunning);
    }

    [Fact]
    public void Phase_labels_are_human_readable()
    {
        var snap = new StartupSequenceTracker().Snapshot;
        foreach (var p in snap.Phases)
        {
            Assert.False(string.IsNullOrEmpty(p.Label));
            Assert.False(p.Label.Contains('_'), $"Label for {p.Phase} should not contain underscores: '{p.Label}'");
        }
    }
}
