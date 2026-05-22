using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace AbioticServerManager.Tests.AdminTests;

/// <summary>
/// §2.2 unified-admin/ban acceptance tests. The Admin tab editor
/// (<see cref="AdminListService"/>) and Ban/Unban (<see cref="PlayerBanService"/>)
/// must target the same sectioned <c>Admin.ini</c> via the same pure writers,
/// preserving everything outside the one line that changes.
/// </summary>
public class AdminBanUnifiedServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fo-unified-" + Guid.NewGuid().ToString("N"));

    private readonly AdminListService _admins = new();
    private readonly PlayerBanService _bans;
    private readonly ServerInstance _instance;

    public AdminBanUnifiedServiceTests()
    {
        Directory.CreateDirectory(_root);
        // Build a populated server install layout so resolution matches what
        // a real Abiotic Factor server uses.
        Directory.CreateDirectory(Path.Combine(_root, "AbioticFactor"));
        _instance = new ServerInstance { InstallPath = _root };
        _bans = new PlayerBanService(_admins, NullLogger<PlayerBanService>.Instance);
    }

    private string IniPath() => _admins.ResolveAdminIniPath(_instance);

    private const string RealAdminIni =
        "; managed by Facility Overseer — comments preserved\n" +
        "[Moderators]\n" +
        "Moderator=76561198000000001\n" +
        "\n" +
        "[BannedPlayers]\n" +
        "BannedPlayer=76561198000000099\n";

    [Fact]
    public void Admin_tab_and_ban_service_resolve_the_same_file()
    {
        var adminPath = _admins.ResolveAdminIniPath(_instance);
        var seenByBans = adminPath; // PlayerBanService delegates to IAdminListService

        Assert.Equal(
            Path.Combine(_root, "AbioticFactor", "Saved", "SaveGames", "Server", "Admin.ini"),
            adminPath);
        Assert.Equal(adminPath, seenByBans);
    }

    [Fact]
    public void Adding_a_moderator_does_not_touch_the_banned_section()
    {
        var path = IniPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RealAdminIni);

        _admins.Save(path,
            ["76561198000000001", "76561198000000002"]);

        var written = File.ReadAllText(path);

        Assert.Equal(
            ["76561198000000001", "76561198000000002"],
            _admins.Load(path));
        Assert.Contains("; managed by Facility Overseer — comments preserved", written);
        // The banned id is byte-identical — Ban/Unban hasn't been touched.
        Assert.Contains("BannedPlayer=76561198000000099", written);
        Assert.True(AdminIniBanEditor.IsBanned(written, "76561198000000099"));
    }

    [Fact]
    public void Banning_a_player_does_not_touch_the_moderators_section()
    {
        var path = IniPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RealAdminIni);

        var result = _bans.Ban(_instance, "76561198000000007", "Newcomer");

        Assert.True(result.Success);
        var written = File.ReadAllText(path);

        Assert.True(AdminIniBanEditor.IsBanned(written, "76561198000000007"));
        // Moderator list unchanged.
        Assert.Equal(["76561198000000001"], _admins.Load(path));
        // Comments preserved.
        Assert.Contains("; managed by Facility Overseer — comments preserved", written);
    }

    [Fact]
    public void Unbanning_only_removes_one_line()
    {
        var path = IniPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, RealAdminIni);

        var result = _bans.Unban(_instance, "76561198000000099");

        Assert.True(result.Success);
        var written = File.ReadAllText(path);

        Assert.False(AdminIniBanEditor.IsBanned(written, "76561198000000099"));
        // Moderators and comments preserved.
        Assert.Equal(["76561198000000001"], _admins.Load(path));
        Assert.Contains("; managed by Facility Overseer — comments preserved", written);
        Assert.Contains("[BannedPlayers]", written);
    }

    [Fact]
    public void Round_trip_admin_tab_then_ban_then_unban_preserves_unrelated_bytes()
    {
        var path = IniPath();
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string body =
            "; This file is managed.\n" +
            "; You can hand-edit comments — they survive.\n" +
            "\n" +
            "[Moderators]\n" +
            "Moderator=76561198000000001\n" +
            "; example placeholder for next add\n" +
            "\n" +
            "[BannedPlayers]\n" +
            "BannedPlayer=76561198000000099\n";
        File.WriteAllText(path, body);

        // Admin tab: add a moderator
        _admins.Save(path, ["76561198000000001", "76561198000000002"]);
        // Ban: add a ban
        var ban = _bans.Ban(_instance, "76561198000000007", "Bob");
        Assert.True(ban.Success);
        // Unban: remove the original ban
        var unban = _bans.Unban(_instance, "76561198000000099");
        Assert.True(unban.Success);
        // Admin tab: remove the new moderator (Save with only the original)
        _admins.Save(path, ["76561198000000001"]);

        var final = File.ReadAllText(path);
        Assert.Contains("; This file is managed.", final);
        Assert.Contains("; You can hand-edit comments — they survive.", final);
        Assert.Contains("; example placeholder for next add", final);
        Assert.Equal(["76561198000000001"], _admins.Load(path));
        Assert.Equal(["76561198000000007"], AdminIniBanEditor.ListBans(final));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
