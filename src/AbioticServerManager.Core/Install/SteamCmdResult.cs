namespace AbioticServerManager.Core.Install;

public sealed record SteamCmdResult
{
    public required bool Success { get; init; }
    public required int ExitCode { get; init; }
    public string Output { get; init; } = "";
    public string? ErrorMessage { get; init; }

    /// <summary>Path to the troubleshooting report written on failure, if any.</summary>
    public string? LogPath { get; init; }

    public static SteamCmdResult Ok(int exitCode, string output) =>
        new() { Success = true, ExitCode = exitCode, Output = output };

    public static SteamCmdResult Fail(int exitCode, string output, string error) =>
        new() { Success = false, ExitCode = exitCode, Output = output, ErrorMessage = error };
}
