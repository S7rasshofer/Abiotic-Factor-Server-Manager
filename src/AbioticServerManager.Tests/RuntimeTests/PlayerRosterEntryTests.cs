using AbioticServerManager.Core.Runtime;

namespace AbioticServerManager.Tests.RuntimeTests;

/// <summary>
/// Pure tests for PlayerRosterEntry derived view properties - in particular the
/// PC/Console heuristic exposed on the Players list. Pinned so the column can
/// trust the values without re-running through the renderer.
/// </summary>
public class PlayerRosterEntryTests
{
    private static PlayerRosterEntry Entry(
        string? steamId = null,
        string? primaryId = null,
        string? platform = null) =>
        new()
        {
            Key = steamId ?? primaryId ?? "k",
            DisplayName = "tester",
            SteamId64 = steamId,
            PrimaryId = primaryId,
            Platform = platform,
        };

    [Fact]
    public void Platform_display_is_pc_steam_when_steamid64_matches()
    {
        var entry = Entry(steamId: "76561198104903704");
        Assert.Equal("PC (Steam)", entry.PlatformDisplay);
    }

    [Fact]
    public void Platform_display_recognises_psn_token()
    {
        var entry = Entry(primaryId: "EOSPlus:PSN:abc", platform: "PSN");
        Assert.Equal("PlayStation", entry.PlatformDisplay);
    }

    [Fact]
    public void Platform_display_recognises_xbox_token()
    {
        var entry = Entry(primaryId: "EOSPlus:XBL:abc", platform: "XBL");
        Assert.Equal("Xbox", entry.PlatformDisplay);
    }

    [Fact]
    public void Platform_display_recognises_epic_token()
    {
        var entry = Entry(primaryId: "EOSPlus:EPIC:abc", platform: "Epic");
        Assert.Equal("PC (Epic)", entry.PlatformDisplay);
    }

    [Fact]
    public void Platform_display_falls_back_to_console_when_only_eos_envelope_is_known()
    {
        // No SteamID64 and the platform field is the generic EOS Plus
        // wrapper - the player is not on Steam, so "Console" is the honest
        // best guess until we capture a more specific token.
        var entry = Entry(primaryId: "EOSPlus:UNKNOWN", platform: "EOSPlus");
        Assert.Equal("Console", entry.PlatformDisplay);
    }

    [Fact]
    public void Platform_display_is_dash_for_name_only_entries()
    {
        var entry = Entry();
        Assert.Equal("-", entry.PlatformDisplay);
    }

    [Fact]
    public void Platform_display_passes_through_unknown_specific_tokens()
    {
        // A non-generic platform token we have not specifically mapped should
        // still show through rather than collapse to "Console" - preserves
        // information for future Abiotic Factor builds that may report a
        // platform string we have not seen yet.
        var entry = Entry(primaryId: "EOSPlus:MOBILE:abc", platform: "iOS");
        Assert.Equal("iOS", entry.PlatformDisplay);
    }
}
