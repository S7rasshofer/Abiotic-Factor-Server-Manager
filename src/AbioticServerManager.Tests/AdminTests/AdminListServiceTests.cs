using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Models;
using AbioticServerManager.Infrastructure.Persistence;

namespace AbioticServerManager.Tests.AdminTests;

public class AdminListServiceTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "fo-admin-" + Guid.NewGuid().ToString("N"));

    private readonly AdminListService _service = new();

    public AdminListServiceTests() => Directory.CreateDirectory(_root);

    [Theory]
    [InlineData("76561198000000000", true)]
    [InlineData("7656119800000000", false)]   // 16 digits
    [InlineData("12345678901234567", false)]  // 17 but not starting with 7
    [InlineData("76561198abcdefghi", false)]
    [InlineData(null, false)]
    public void Validates_steam_ids(string? value, bool expected) =>
        Assert.Equal(expected, IAdminListService.IsValidSteamId(value));

    [Fact]
    public void Resolve_falls_back_to_installed_server_layout_when_no_explicit_path()
    {
        // §2.1: when AdminIniPath is unset (legacy world that has not yet been
        // migrated), fall back to the dedicated server's Admin.ini location.
        // Once the world-identity migration runs, AdminIniPath is populated and
        // wins (see Resolve_prefers_explicit_admin_ini_path_under_data_root).
        var install = Path.Combine(_root, "server");
        Directory.CreateDirectory(Path.Combine(install, "AbioticFactor"));

        var path = _service.ResolveAdminIniPath(new ServerInstance
        {
            InstallPath = install,
            AdminIniPath = "",
        });

        Assert.Equal(
            Path.Combine(install, "AbioticFactor", "Saved", "SaveGames", "Server", "Admin.ini"),
            path);
    }

    [Fact]
    public void Resolve_prefers_explicit_admin_ini_path_under_data_root()
    {
        // §2.1: after the world-identity migration, AdminIniPath points at
        // <DataRoot>/worlds/<id>/config/Admin.ini and that path wins over the
        // in-install legacy location. A SteamCMD validate or server reinstall
        // cannot route us back to a freshly-wiped in-install path.
        var install = Path.Combine(_root, "server");
        Directory.CreateDirectory(Path.Combine(install, "AbioticFactor"));
        var migrated = Path.Combine(_root, "worlds", "w1", "config", "Admin.ini");

        var path = _service.ResolveAdminIniPath(new ServerInstance
        {
            InstallPath = install,
            AdminIniPath = migrated,
        });

        Assert.Equal(migrated, path);
    }

    [Fact]
    public void Resolve_prefers_explicit_then_sandbox_then_install_without_server_layout()
    {
        Assert.Equal(
            @"C:\x\Admin.ini",
            _service.ResolveAdminIniPath(new ServerInstance { AdminIniPath = @"C:\x\Admin.ini" }));

        // §2.2: when only a sandbox path is known, the admin file is the
        // sectioned Admin.ini sibling — never the legacy flat "Admins.ini".
        var bySandbox = _service.ResolveAdminIniPath(
            new ServerInstance { SandboxIniPath = Path.Combine(_root, "World", "SandboxSettings.ini") });
        Assert.Equal(Path.Combine(_root, "World", "Admin.ini"), bySandbox);

        var byInstall = _service.ResolveAdminIniPath(new ServerInstance { InstallPath = _root });
        Assert.Equal(Path.Combine(_root, "Admin.ini"), byInstall);
    }

    [Fact]
    public void Missing_file_loads_empty()
    {
        Assert.Empty(_service.Load(Path.Combine(_root, "nope.ini")));
    }

    [Fact]
    public void Load_reads_only_the_moderators_section()
    {
        // §2.2: a real Abiotic Factor Admin.ini is sectioned. Banned IDs must
        // not leak into the moderator list, and bare/legacy lines outside any
        // section are not moderators.
        var path = Path.Combine(_root, "Admin.ini");
        File.WriteAllText(path,
            "; comment kept verbatim\n" +
            "[Moderators]\n" +
            "Moderator=76561198000000001\n" +
            "Moderator=76561198000000002\n" +
            "\n" +
            "[BannedPlayers]\n" +
            "BannedPlayer=76561198000000099\n");

        Assert.Equal(
            ["76561198000000001", "76561198000000002"],
            _service.Load(path));
    }

    [Fact]
    public void Save_preserves_comments_blank_lines_and_banned_section_byte_for_byte()
    {
        var path = Path.Combine(_root, "sub", "Admin.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        const string original =
            "; keep me\n" +
            "[Moderators]\n" +
            "Moderator=76561198000000009\n" +
            "; inline example — keep me too\n" +
            "\n" +
            "[BannedPlayers]\n" +
            "BannedPlayer=76561198000000077\n";
        File.WriteAllText(path, original);

        _service.Save(path, ["76561198000000001", "76561198000000002", "bad"]);

        var written = File.ReadAllText(path);
        // moderators rewritten
        Assert.Equal(
            ["76561198000000001", "76561198000000002"],
            _service.Load(path));
        // everything else preserved byte-for-byte
        Assert.Contains("; keep me", written);
        Assert.Contains("; inline example — keep me too", written);
        Assert.Contains("[BannedPlayers]", written);
        Assert.Contains("BannedPlayer=76561198000000077", written);
    }

    [Fact]
    public void Save_creates_file_with_moderators_section_when_missing()
    {
        var path = Path.Combine(_root, "fresh", "Admin.ini");

        _service.Save(path, ["76561198000000001"]);

        Assert.True(File.Exists(path));
        Assert.Equal(["76561198000000001"], _service.Load(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
