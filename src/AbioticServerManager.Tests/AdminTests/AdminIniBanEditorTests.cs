using AbioticServerManager.Core.Admin;

namespace AbioticServerManager.Tests.AdminTests;

public class AdminIniBanEditorTests
{
    private const string RealAdminIni =
        "[Moderators]\n" +
        "Moderator=ExampleID1\n" +
        "Moderator=ExampleID2\n" +
        "\n" +
        "[BannedPlayers]\n" +
        "BannedPlayer=ExampleBanID1\n" +
        "BannedPlayer=ExampleBanID2\n";

    [Fact]
    public void Lists_only_banned_section_ids()
    {
        var bans = AdminIniBanEditor.ListBans(RealAdminIni);

        Assert.Equal(["ExampleBanID1", "ExampleBanID2"], bans);
    }

    [Fact]
    public void Add_ban_inserts_into_existing_section_and_preserves_the_rest()
    {
        var updated = AdminIniBanEditor.AddBan(RealAdminIni, "76561198104903704");

        Assert.True(AdminIniBanEditor.IsBanned(updated, "76561198104903704"));
        Assert.Contains("[Moderators]", updated);
        Assert.Contains("Moderator=ExampleID1", updated);
        Assert.Contains("BannedPlayer=ExampleBanID1", updated);
        Assert.Contains("BannedPlayer=76561198104903704", updated);
        // The new id is inside the banned section, after the existing entries.
        var lines = updated.Replace("\r", "").Split('\n');
        var sec = Array.IndexOf(lines, "[BannedPlayers]");
        var idx = Array.IndexOf(lines, "BannedPlayer=76561198104903704");
        Assert.True(idx > sec);
    }

    [Fact]
    public void Add_ban_is_idempotent()
    {
        var once = AdminIniBanEditor.AddBan(RealAdminIni, "ABC");
        var twice = AdminIniBanEditor.AddBan(once, "ABC");

        Assert.Equal(once, twice);
        Assert.Single(AdminIniBanEditor.ListBans(twice), b => b == "ABC");
    }

    [Fact]
    public void Add_ban_creates_section_when_missing()
    {
        const string onlyMods = "[Moderators]\nModerator=Keep\n";

        var updated = AdminIniBanEditor.AddBan(onlyMods, "NEWID");

        Assert.Contains("Moderator=Keep", updated);
        Assert.Contains("[BannedPlayers]", updated);
        Assert.True(AdminIniBanEditor.IsBanned(updated, "NEWID"));
    }

    [Fact]
    public void Remove_ban_drops_only_that_id()
    {
        var updated = AdminIniBanEditor.RemoveBan(RealAdminIni, "ExampleBanID1");

        Assert.False(AdminIniBanEditor.IsBanned(updated, "ExampleBanID1"));
        Assert.True(AdminIniBanEditor.IsBanned(updated, "ExampleBanID2"));
        Assert.Contains("Moderator=ExampleID1", updated);
    }

    [Fact]
    public void Remove_ban_ignores_a_moderator_with_the_same_value()
    {
        const string ini = "[Moderators]\nModerator=SHARED\n\n[BannedPlayers]\nBannedPlayer=SHARED\n";

        var updated = AdminIniBanEditor.RemoveBan(ini, "SHARED");

        Assert.Contains("Moderator=SHARED", updated);          // moderator kept
        Assert.False(AdminIniBanEditor.IsBanned(updated, "SHARED")); // only ban removed
    }
}
