using AbioticServerManager.Infrastructure.FileSystem;

namespace AbioticServerManager.Tests.FileSystemTests;

public class AppPathsTests
{
    [Fact]
    public void Single_root_layout_keeps_every_managed_file_under_the_data_root()
    {
        var root = Path.Combine(Path.GetTempPath(), "fo-paths-" + Guid.NewGuid().ToString("N"));
        var paths = new AppPaths(root);

        Assert.Equal(root, paths.DataRoot);
        Assert.Equal(Path.Combine(root, "config"), paths.ConfigDirectory);
        Assert.Equal(Path.Combine(root, "config", "instances.json"), paths.InstancesFile);
        Assert.Equal(Path.Combine(root, "tools", "steamcmd"), paths.SteamCmdDirectory);
        Assert.Equal(
            Path.Combine(root, "servers", "abiotic-factor-dedicated"),
            paths.DefaultServerInstallDirectory);
        Assert.Equal(Path.Combine(root, "backups"), paths.BackupsRoot);
        Assert.Equal(Path.Combine(root, "logs"), paths.LogsDirectory);
        Assert.Equal(Path.Combine(root, "players"), paths.PlayersDirectory);
    }

    [Theory]
    // Writable and not cloud-synced -> portable folder beside the exe.
    [InlineData(true, false, true)]
    // Not writable (e.g. Program Files) -> safe local app data folder.
    [InlineData(false, false, false)]
    // Writable but cloud-synced (OneDrive et al.) -> still falls back to local
    // app data, so SteamCMD's steam.dll never lives on a synced volume.
    [InlineData(true, true, false)]
    public void Data_root_resolver_uses_the_portable_folder_only_when_safe(
        bool appDirectoryWritable,
        bool appDirectorySynced,
        bool expectsPortableFolder)
    {
        var appBase = Path.Combine(Path.GetTempPath(), "fo-app");
        var local = Path.Combine(Path.GetTempPath(), "fo-local");

        var resolved = AppPaths.ResolveDataRoot(
            appBase, local, appDirectoryWritable, appDirectorySynced);

        var expected = expectsPortableFolder
            ? Path.Combine(appBase, "FacilityOverseerData")
            : Path.Combine(local, "FacilityOverseer");
        Assert.Equal(expected, resolved);
    }

    [Theory]
    [InlineData(@"C:\Users\bob\OneDrive\Documents\App", true)]
    [InlineData(@"C:\Users\bob\OneDrive - Contoso\App", true)]
    [InlineData(@"C:\Users\bob\Dropbox\App", true)]
    [InlineData(@"C:\Users\bob\Google Drive\App", true)]
    [InlineData(@"C:\Users\bob\AppData\Local\FacilityOverseer", false)]
    [InlineData(@"C:\FacilityOverseer", false)]
    [InlineData("", false)]
    public void Detects_synced_locations(string path, bool expected) =>
        Assert.Equal(expected, AppPaths.IsSyncedLocation(path));

    [Fact]
    public void Ensure_created_does_not_create_managed_server_payload_folder()
    {
        var root = Path.Combine(Path.GetTempPath(), "fo-paths-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = new AppPaths(root);

            paths.EnsureCreated();

            Assert.True(Directory.Exists(paths.ConfigDirectory));
            Assert.True(Directory.Exists(paths.SteamCmdDirectory));
            Assert.True(Directory.Exists(paths.ServersDirectory));
            Assert.False(Directory.Exists(paths.ManagedServerDirectory));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
