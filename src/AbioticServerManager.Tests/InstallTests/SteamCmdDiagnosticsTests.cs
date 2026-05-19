using AbioticServerManager.Core.Install;

namespace AbioticServerManager.Tests.InstallTests;

public class SteamCmdDiagnosticsTests
{
    [Theory]
    [InlineData("[----] Failed to apply update, reverting...")]
    [InlineData(" !!! Fatal Error: Failed to load steam.dll")]
    [InlineData("Failed to load steam.dll")]
    [InlineData("...!!! Fatal Error something else")]
    public void Detects_self_update_failure(string output) =>
        Assert.True(SteamCmdDiagnostics.LooksLikeSelfUpdateFailure(output));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    [InlineData("Success! App '2857200' fully installed.")]
    [InlineData("Update state (0x61) downloading, progress: 42.13")]
    public void Ignores_normal_or_empty_output(string? output) =>
        Assert.False(SteamCmdDiagnostics.LooksLikeSelfUpdateFailure(output));

    [Theory]
    [InlineData("BCommitUpdatedFiles: failed to rename package\\tmp\\.\\crashhandler.dll_ -> ./crashhandler.dll (error 32)")]
    [InlineData("failed to rename foo -> bar")]
    [InlineData("something (error 32)")]
    public void Detects_locked_files_sharing_violation(string output)
    {
        Assert.True(SteamCmdDiagnostics.LooksLikeLockedFiles(output));
        Assert.True(SteamCmdDiagnostics.LooksLikeSelfUpdateFailure(output));
    }

    [Fact]
    public void Summarize_calls_out_locked_files_specifically()
    {
        var s = SteamCmdDiagnostics.Summarize(
            "BCommitUpdatedFiles: failed to rename x -> y (error 32)");

        Assert.Contains("error 32", s);
        Assert.Contains("locked", s);
    }

    [Fact]
    public void Summarize_is_safe_on_unknown_output() =>
        Assert.False(string.IsNullOrWhiteSpace(SteamCmdDiagnostics.Summarize("random")));

    [Fact]
    public void Help_text_is_actionable_about_sync_and_antivirus()
    {
        var help = SteamCmdDiagnostics.SelfUpdateHelp;

        Assert.Contains("steam.dll", help);
        Assert.Contains("OneDrive", help);
        Assert.Contains("antivirus", help);
    }
}
