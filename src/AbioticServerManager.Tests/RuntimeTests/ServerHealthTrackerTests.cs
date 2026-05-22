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
    public void Blocked_state_is_sticky_over_later_readiness()
    {
        var t = new ServerHealthTracker();
        t.OnProcessStarted();
        t.Apply(Log("Failed to bind: address already in use"));
        Assert.Equal(ServerHealth.Blocked, t.Health);

        t.Apply(Log("Session creation completed."));
        Assert.Equal(ServerHealth.Blocked, t.Health);
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

    [Fact]
    public void Normal_line_is_not_blocking_or_ready()
    {
        Assert.Null(ServerHealthSignals.BlockingReason("LogMemory: tick complete"));
        Assert.False(ServerHealthSignals.IsReadiness("LogMemory: tick complete"));
    }

    [Theory]
    [InlineData("LogAbiotic: World save is corrupt and cannot be loaded", "world.corrupt")]
    [InlineData("Failed to bind: address already in use", "port.bind_fail")]
    public void BlockingTag_maps_recoverable_signals(string text, string expected) =>
        Assert.Equal(expected, ServerHealthSignals.BlockingTag(text));

    [Theory]
    [InlineData("Could not find sandbox override path")]
    [InlineData("EOS error: session creation failed")]
    [InlineData("Fatal error encountered")]
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
}
