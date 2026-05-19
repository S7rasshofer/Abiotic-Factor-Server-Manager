using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.App.ViewModels;

public sealed record ServerLogEntry(
    DateTimeOffset Timestamp,
    string Text,
    bool IsError,
    ServerLogSeverity Severity)
{
    public string DisplayText => $"{Timestamp:HH:mm:ss}  {Text}";

    public static ServerLogEntry From(ServerLogLine line) =>
        new(line.Timestamp, line.Text, line.IsError, line.Severity);
}
