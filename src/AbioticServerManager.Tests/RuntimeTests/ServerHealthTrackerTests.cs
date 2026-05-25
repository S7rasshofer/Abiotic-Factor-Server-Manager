using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.RuntimeTests;

public class ServerHealthTrackerTests
{
    private static ServerLogLine Log(string text) =>
        new("w", DateTimeOffset.UtcNow, text, IsError: false);

    [Fact]
    public void Starts_stopped_then_starting_then_online()
    {
        var t = new ServerHealthTracker();
        Assert.Equal(ServerHealth.Stopped, t.Health);

        t.OnProcessStarted();
        Assert.Equal(ServerHealth.Starting, t.Health);

        Assert.True(t.Apply(Log("LogEOS: Session creation completed.")));
        Assert.Equal(ServerHealth.Online, t.Health);
    }

    [Fact]
    public void Blocking_signal_moves_to_blocked_with_reason()
    {
        var t = new ServerHealthTracker();
        t.OnProcessStarted();

        Assert.True(t.Apply(Log("LogAbiotic: World save is corrupt and cannot be loaded")));
        Assert.Equal(ServerHealth.Blocked, t.Health);
        Assert.Contains("corrupt", t.Reason);
    }

    [Fact]
    public void Blocked_recovers_to_online_when_later_readiness_arrives()
    {
        // Regression: the old one-way trap pinned the UI to red forever even
        // after the server resolved a transient blocking line on its own. A
        // later readiness signal is direct evidence the server is up.
        var t = new ServerHealthTracker();
        t.OnProcessStarted();
        t.Apply(Log("Failed to bind: address already in use"));
        Assert.Equal(ServerHealth.Blocked, t.Health);

        t.Apply(Log("Session creation completed."));
        Assert.Equal(ServerHealth.Online, t.Health);
        Assert.Null(t.BlockingTag);
    }

    [Fact]
    public void Confirm_online_from_corroboration_promotes_blocked_to_online()
    {
        var t = new ServerHealthTracker();
        t.OnProcessStarted();
        t.Apply(Log("LogAbiotic: World save is corrupt and cannot be loaded"));
        Assert.Equal(ServerHealth.Blocked, t.Health);

        Assert.True(t.ConfirmOnlineFromCorroboration("A2S responded on 127.0.0.1:27015"));
        Assert.Equal(ServerHealth.Online, t.Health);
        Assert.Null(t.BlockingTag);
        Assert.Contains("A2S", t.Reason);
    }

    [Fact]
    public void Confirm_online_from_corroboration_promotes_starting_to_online()
    {
        var t = new ServerHealthTracker();
        t.OnProcessStarted();
        Assert.Equal(ServerHealth.Starting, t.Health);

        Assert.True(t.ConfirmOnlineFromCorroboration("EOS lobby code observed"));
        Assert.Equal(ServerHealth.Online, t.Health);
    }

    [Fact]
    public void Confirm_online_from_corroboration_is_noop_when_stopped_or_crashed()
    {
        var t = new ServerHealthTracker();
        Assert.False(t.ConfirmOnlineFromCorroboration("ignored while stopped"));
        Assert.Equal(ServerHealth.Stopped, t.Health);

        t.OnProcessStarted();
        t.OnProcessExited(unexpected: true);
        Assert.Equal(ServerHealth.Crashed, t.Health);
        Assert.False(t.ConfirmOnlineFromCorroboration("ignored while crashed"));
        Assert.Equal(ServerHealth.Crashed, t.Health);
    }

    [Fact]
    public void Unexpected_exit_is_crashed()
    {
        var t = new ServerHealthTracker();
        t.OnProcessStarted();
        t.OnProcessExited(unexpected: true);
        Assert.Equal(ServerHealth.Crashed, t.Health);
    }

    [Fact]
    public void Clean_exit_is_stopped()
    {
        var t = new ServerHealthTracker();
        t.OnProcessStarted();
        t.OnProcessExited(unexpected: false);
        Assert.Equal(ServerHealth.Stopped, t.Health);
    }

    [Fact]
    public void Log_noise_while_stopped_is_ignored()
    {
        var t = new ServerHealthTracker();
        Assert.False(t.Apply(Log("Session creation completed.")));
        Assert.Equal(ServerHealth.Stopped, t.Health);
    }

    [Theory]
    [InlineData("LogSockets: Failed to bind socket on 0.0.0.0:7777")]
    [InlineData("Bind failed: address already in use")]
    [InlineData("LogNet: Could not bind UDP listen socket")]
    [InlineData("LogSockets: address already in use during listen")]
    [InlineData("LogSockets: bind: WSAEADDRINUSE")]
    [InlineData("Could not find sandbox override path")]
    [InlineData("EOS Session creation failed: EOS_NotFound")]
    [InlineData("session: failed to create the online lobby")]
    [InlineData("Fatal error encountered, aborting")]
    [InlineData("LogWindows: Error: === Critical error: ===")]
    [InlineData("Assertion failed: SessionInterface.cpp")]
    public void Recognises_blocking_reasons(string text) =>
        Assert.NotNull(ServerHealthSignals.BlockingReason(text));

    /// <summary>
    /// Regression: the previous rules false-positived on benign AF/EOS runtime
    /// log lines whose substrings overlapped with the broad triggers
    /// (`port`+`in use`, `session`+`EOS`+`error`, bare `fatal error`). A
    /// transient match pinned the UI to red even though the server was online.
    /// </summary>
    [Theory]
    [InlineData("LogQueryService: Steam query port 27015 in use by AbioticFactorServer")]
    [InlineData("LogPortMapper: Port 7777 currently in use; reusing existing reservation")]
    [InlineData("LogEOSSession: Verbose: SetSessionAttribute returned error code SUCCESS")]
    [InlineData("LogEOSSession: Warning: session refresh error: retry scheduled")]
    [InlineData("LogEOSCore: Error: transient login retry; recovered")]
    [InlineData("LogStats: Fatal error category recorded: none")]
    [InlineData("LogAbiotic: Net driver listening on port 7777")]
    public void Benign_runtime_lines_are_not_blocking(string text)
    {
        Assert.Null(ServerHealthSignals.BlockingReason(text));
        Assert.Null(ServerHealthSignals.BlockingTag(text));
    }

    [Fact]
    public void Normal_line_is_not_blocking_or_ready()
    {
        Assert.Null(ServerHealthSignals.BlockingReason("LogMemory: tick complete"));
        Assert.False(ServerHealthSignals.IsReadiness("LogMemory: tick complete"));
    }

    [Theory]
    [InlineData("LogAbiotic: World save is corrupt and cannot be loaded", "world.corrupt")]
    [InlineData("LogSockets: Failed to bind socket on 0.0.0.0:7777", "port.bind_fail")]
    [InlineData("LogNet: Could not bind UDP listen socket", "port.bind_fail")]
    public void BlockingTag_maps_recoverable_signals(string text, string expected) =>
        Assert.Equal(expected, ServerHealthSignals.BlockingTag(text));

    [Theory]
    [InlineData("Could not find sandbox override path")]
    [InlineData("EOS Session creation failed: EOS_NotFound")]
    [InlineData("Fatal error encountered, aborting")]
    [InlineData("LogMemory: tick complete")]
    public void BlockingTag_is_null_when_there_is_no_guided_flow(string text) =>
        Assert.Null(ServerHealthSignals.BlockingTag(text));

    [Fact]
    public void Tracker_exposes_blocking_tag_while_blocked()
    {
        var t = new ServerHealthTracker();
        t.OnProcessStarted();
        Assert.Null(t.BlockingTag);

        t.Apply(Log("LogAbiotic: the world save is corrupt"));
        Assert.Equal(ServerHealth.Blocked, t.Health);
        Assert.Equal("world.corrupt", t.BlockingTag);
    }

    /// <summary>
    /// Regression: a fresh, healthy world used to flip to Blocked because
    /// routine startup probes mention "world" together with the word
    /// "corruption". The tightened rule requires the line to actually express
    /// the failure, not the absence of it.
    /// </summary>
    [Theory]
    [InlineData("LogAbiotic: World integrity check passed - no corruption detected")]
    [InlineData("LogWorld: World loaded, no corruption found")]
    [InlineData("LogSaveGame: Verifying world save (checking for corruption)")]
    [InlineData("LogStreaming: No corrupt packages in world bundle")]
    [InlineData("LogAbiotic: Pre-flight: world corruption probe complete")]
    public void Benign_world_plus_corrupt_lines_are_not_blocking(string text)
    {
        Assert.Null(ServerHealthSignals.BlockingReason(text));
        Assert.Null(ServerHealthSignals.BlockingTag(text));
    }

    /// <summary>
    /// Real corruption fatals stay matched after the tightening. Each line
    /// expresses the failure via a contiguous "X is corrupt" / "corrupt X"
    /// phrase, or pairs the corruption keyword with an explicit failure verb.
    /// </summary>
    [Theory]
    [InlineData("LogAbiotic: World save is corrupt and cannot be loaded")]
    [InlineData("LogAbiotic: the world save is corrupt")]
    [InlineData("LogSaveGame: Save file is corrupt - aborted")]
    [InlineData("LogSaveGame: Failed to load save game (corruption detected)")]
    [InlineData("LogSaveGame: Save corruption detected, load failed")]
    [InlineData("LogAbiotic: Fatal: corrupt save data")]
    public void Real_corruption_signals_still_block(string text)
    {
        Assert.NotNull(ServerHealthSignals.BlockingReason(text));
        Assert.Equal("world.corrupt", ServerHealthSignals.BlockingTag(text));
    }
}
