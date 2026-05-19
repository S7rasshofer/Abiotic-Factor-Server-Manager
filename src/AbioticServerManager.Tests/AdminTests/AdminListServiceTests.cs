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
    public void Resolve_prefers_installed_server_layout()
    {
        var install = Path.Combine(_root, "server");
        Directory.CreateDirectory(Path.Combine(install, "AbioticFactor"));

        var path = _service.ResolveAdminIniPath(new ServerInstance
        {
            InstallPath = install,
            AdminIniPath = @"C:\old\Admins.ini",
        });

        Assert.Equal(
            Path.Combine(install, "AbioticFactor", "Saved", "SaveGames", "Server", "Admin.ini"),
            path);
    }

    [Fact]
    public void Resolve_prefers_explicit_then_sandbox_then_install_without_server_layout()
    {
        Assert.Equal(
            @"C:\x\Admins.ini",
            _service.ResolveAdminIniPath(new ServerInstance { AdminIniPath = @"C:\x\Admins.ini" }));

        var bySandbox = _service.ResolveAdminIniPath(
            new ServerInstance { SandboxIniPath = Path.Combine(_root, "World", "SandboxSettings.ini") });
        Assert.Equal(Path.Combine(_root, "World", "Admins.ini"), bySandbox);

        var byInstall = _service.ResolveAdminIniPath(new ServerInstance { InstallPath = _root });
        Assert.Equal(Path.Combine(_root, "Admin.ini"), byInstall);
    }

    [Fact]
    public void Missing_file_loads_empty()
    {
        Assert.Empty(_service.Load(Path.Combine(_root, "nope.ini")));
    }

    [Fact]
    public void Load_keeps_only_valid_ids_and_dedupes()
    {
        var path = Path.Combine(_root, "Admins.ini");
        File.WriteAllLines(path,
        [
            "; Server admins",
            "76561198000000001",
            "",
            "not-an-id",
            "76561198000000001",
            "76561198000000002",
        ]);

        Assert.Equal(
            ["76561198000000001", "76561198000000002"],
            _service.Load(path));
    }

    [Fact]
    public void Save_preserves_comments_and_round_trips()
    {
        var path = Path.Combine(_root, "sub", "Admins.ini");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllLines(path,
        [
            "; keep me",
            "76561198000000009",
        ]);

        _service.Save(path, ["76561198000000001", "76561198000000002", "bad"]);

        var written = File.ReadAllLines(path);
        Assert.Contains("; keep me", written);
        Assert.Equal(
            ["76561198000000001", "76561198000000002"],
            _service.Load(path));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }
}
