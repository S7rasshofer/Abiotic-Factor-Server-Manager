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
    public void Readiness_after_blocking_signal_recovers_to_online()
    {
        var t = new ServerHealthTracker();
        t.OnProcessStarted();
        t.Apply(Log("Failed to bind: address already in use"));
        Assert.Equal(ServerHealth.Blocked, t.Health);

        // A server that goes on to report readiness is, by definition, online;
        // an earlier blocking signal must not latch permanently.
        Assert.True(t.Apply(Log("Session creation completed.")));
        Assert.Equal(ServerHealth.Online, t.Health);
    }

    [Fact]
    public void Benign_corruption_line_does_not_block_a_starting_server()
    {
        var t = new ServerHealthTracker();
        t.OnProcessStarted();

        // The save-backup system mentions "corruption" on healthy servers.
        Assert.False(t.Apply(Log(
            "LogWorldSave: Backup written to guard the world against corruption")));
        Assert.Equal(ServerHealth.Starting, t.Health);

        Assert.True(t.Apply(Log("LogEOS: Session creation completed.")));
        Assert.Equal(ServerHealth.Online, t.Health);
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
    [InlineData("port 7777 already in use")]
    [InlineData("Could not find sandbox override path")]
    [InlineData("EOS error: session creation failed")]
    [InlineData("Fatal error encountered")]
    public void Recognises_blocking_reasons(string text) =>
        Assert.NotNull(ServerHealthSignals.BlockingReason(text));

    [Theory]
    [InlineData("LogAbiotic: Error - world save is corrupt and cannot be loaded")]
    [InlineData("LogAbiotic: Fatal: failed to load world, save data is corrupt")]
    public void Fatal_world_corruption_still_blocks(string text) =>
        Assert.NotNull(ServerHealthSignals.BlockingReason(text));

    [Theory]
    [InlineData("LogAbiotic: Restored world save from a backup after detecting corruption")]
    [InlineData("LogAbiotic: World save integrity check passed, no corruption found")]
    [InlineData("LogWorldSave: Backup written to guard the world against corruption")]
    public void Benign_corruption_mentions_do_not_block(string text) =>
        Assert.Null(ServerHealthSignals.BlockingReason(text));

    [Fact]
    public void Normal_line_is_not_blocking_or_ready()
    {
        Assert.Null(ServerHealthSignals.BlockingReason("LogMemory: tick complete"));
        Assert.False(ServerHealthSignals.IsReadiness("LogMemory: tick complete"));
    }
}
