namespace AbioticServerManager.Core.Install;

/// <summary>
/// Pure recognition of SteamCMD's self-update death spiral. SteamCMD rewrites
/// its own <c>steam.dll</c>/<c>steamcmd.exe</c> in place; if a sync client
/// (OneDrive/Dropbox/Google Drive) or antivirus locks or dehydrates those files
/// mid-write, the update reverts and the next launch cannot load steam.dll.
/// Retrying the same command never fixes an already-broken binary, so the app
/// must detect this and self-heal / explain it instead of surfacing a bare
/// "exited with code -1".
/// </summary>
public static class SteamCmdDiagnostics
{
    private static readonly string[] SelfUpdateFailureMarkers =
    [
        "Failed to load steam.dll",
        "Failed to apply update, reverting",
        "!!! Fatal Error",
        "Fatal Error: Failed to load steam.dll",
    ];

    // SteamCMD's bootstrap log signature when a sync client / AV holds its files
    // open: "BCommitUpdatedFiles: failed to rename ... (error 32)". Win32 error 32
    // is ERROR_SHARING_VIOLATION - the file is locked by another process.
    private static readonly string[] LockedFileMarkers =
    [
        "BCommitUpdatedFiles",
        "failed to rename",
        "(error 32)",
        "error 32)",
    ];

    public static bool LooksLikeSelfUpdateFailure(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        foreach (var marker in SelfUpdateFailureMarkers)
        {
            if (output.Contains(marker, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return LooksLikeLockedFiles(output);
    }

    /// <summary>
    /// True when SteamCMD could not replace its own files because another process
    /// (cloud sync / antivirus) had them locked - Windows error 32.
    /// </summary>
    public static bool LooksLikeLockedFiles(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return false;
        }

        var hasRenameFailure =
            output.Contains("BCommitUpdatedFiles", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("failed to rename", StringComparison.OrdinalIgnoreCase);
        var hasSharingViolation =
            output.Contains("(error 32)", StringComparison.OrdinalIgnoreCase) ||
            output.Contains("error 32)", StringComparison.OrdinalIgnoreCase);

        return hasRenameFailure || hasSharingViolation;
    }

    /// <summary>One-line plain-language cause for the failure dialog and the log header.</summary>
    public static string Summarize(string? output)
    {
        if (LooksLikeLockedFiles(output))
        {
            return "SteamCMD could not replace its own files because another process had " +
                   "them locked (Windows error 32, sharing violation). On a OneDrive / " +
                   "Dropbox / Documents folder this is the sync client or antivirus.";
        }

        if (LooksLikeSelfUpdateFailure(output))
        {
            return "SteamCMD failed to self-update and could not load steam.dll, so it " +
                   "cannot download server files.";
        }

        return "SteamCMD did not complete successfully.";
    }

    /// <summary>Actionable, user-facing remediation for the self-update failure.</summary>
    public static string SelfUpdateHelp =>
        "SteamCMD could not update itself - it failed to replace steam.dll. This almost " +
        "always means its folder is being synced or locked by another program." +
        Environment.NewLine + Environment.NewLine +
        "Fix it by doing ONE of these, then try again:" + Environment.NewLine +
        " - Move Facility Overseer and its data out of OneDrive / Documents to a plain " +
        "local path such as C:\\FacilityOverseer, or" + Environment.NewLine +
        " - Exclude the Facility Overseer data folder from OneDrive (and from antivirus " +
        "real-time scanning), then retry." + Environment.NewLine + Environment.NewLine +
        "Facility Overseer already tried a clean SteamCMD reinstall automatically.";
}
