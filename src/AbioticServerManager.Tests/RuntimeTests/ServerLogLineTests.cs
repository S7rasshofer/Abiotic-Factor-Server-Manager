using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.RuntimeTests;

public class ServerLogLineTests
{
    [Theory]
    [InlineData("LogAbiotic: Error: Could not find sandbox override path")]
    [InlineData("Load failed: libraryfolders.vdf")]
    [InlineData("[server exited unexpectedly with code 1]")]
    [InlineData("Unhandled exception while loading world")]
    public void Detects_error_signals_in_regular_output(string text)
    {
        var line = ServerLogLine.FromProcessOutput(
            "world",
            DateTimeOffset.Parse("2026-05-18T12:00:00Z"),
            text,
            isErrorStream: false);

        Assert.True(line.IsError);
    }

    [Fact]
    public void Treats_stderr_as_error()
    {
        var line = ServerLogLine.FromProcessOutput(
            "world",
            DateTimeOffset.Parse("2026-05-18T12:00:00Z"),
            "plain stderr text",
            isErrorStream: true);

        Assert.True(line.IsError);
    }

    [Fact]
    public void Leaves_normal_log_lines_unmarked()
    {
        var line = ServerLogLine.FromProcessOutput(
            "world",
            DateTimeOffset.Parse("2026-05-18T12:00:00Z"),
            "LogOnlineSession: Display: Session creation completed.",
            isErrorStream: false);

        Assert.False(line.IsError);
    }

    [Fact]
    public void Leaves_warning_only_failure_lines_unmarked()
    {
        var line = ServerLogLine.FromProcessOutput(
            "world",
            DateTimeOffset.Parse("2026-05-18T12:00:00Z"),
            "LogStringTable: Warning: Failed to find string table entry.",
            isErrorStream: false);

        Assert.False(line.IsError);
    }

    private static ServerLogLine Line(string text, bool stderr = false) =>
        ServerLogLine.FromProcessOutput(
            "world", DateTimeOffset.Parse("2026-05-18T12:00:00Z"), text, stderr);

    [Theory]
    [InlineData("LogStringTable: Warning: Failed to find string table entry.")]
    [InlineData("LogCore: warn: low memory")]
    [InlineData("API is deprecated, use the new one")]
    public void Warning_lines_get_warning_severity(string text)
    {
        var line = Line(text);

        Assert.False(line.IsError);
        Assert.Equal(ServerLogSeverity.Warning, line.Severity);
    }

    [Theory]
    [InlineData("LogAbiotic: Error: Could not find sandbox override path", false)]
    [InlineData("plain stderr text", true)]
    public void Error_lines_get_error_severity(string text, bool stderr) =>
        Assert.Equal(ServerLogSeverity.Error, Line(text, stderr).Severity);

    [Fact]
    public void Normal_lines_get_info_severity() =>
        Assert.Equal(
            ServerLogSeverity.Info,
            Line("LogOnlineSession: Display: Session creation completed.").Severity);

    [Fact]
    public void Error_wins_over_warning_when_both_present() =>
        Assert.Equal(
            ServerLogSeverity.Error,
            Line("Warning and Error: fatal exception thrown").Severity);
}
