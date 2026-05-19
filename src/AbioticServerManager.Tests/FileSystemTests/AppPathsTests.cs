using AbioticServerManager.Infrastructure.FileSystem;

namespace AbioticServerManager.Tests.FileSystemTests;

public class AppPathsTests
{
    [Fact]
    public void Single_root_layout_keeps_mutable_files_under_data_root()
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
    }

    [Theory]
    [InlineData(true, "FacilityOverseerData")]
    [InlineData(false, "FacilityOverseer")]
    public void Data_root_resolver_prefers_portable_folder_when_app_directory_is_writable(
        bool appDirectoryWritable,
        string expectedLeaf)
    {
        var appBase = Path.Combine(Path.GetTempPath(), "fo-app");
        var local = Path.Combine(Path.GetTempPath(), "fo-local");

        var resolved = AppPaths.ResolveDataRoot(appBase, local, appDirectoryWritable);

        Assert.Equal(Path.Combine(appDirectoryWritable ? appBase : local, expectedLeaf), resolved);
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
    public void Synced_root_redirects_tools_and_servers_but_keeps_config()
    {
        var oneDriveRoot = Path.Combine(
            @"C:\Users\bob\OneDrive\Documents", "FacilityOverseerData");

        var paths = new AppPaths(oneDriveRoot);

        // Config stays put (small, sync-safe, avoids losing the user's worlds)...
        Assert.Equal(oneDriveRoot, paths.DataRoot);
        Assert.StartsWith(oneDriveRoot, paths.ConfigDirectory);
        // ...but rewrite-heavy SteamCMD/servers move off the synced volume.
        Assert.NotEqual(oneDriveRoot, paths.VolatileRoot);
        Assert.False(AppPaths.IsSyncedLocation(paths.SteamCmdDirectory));
        Assert.DoesNotContain("OneDrive", paths.SteamCmdDirectory);
        Assert.DoesNotContain("OneDrive", paths.ServersDirectory);
    }

    [Fact]
    public void Non_synced_root_keeps_everything_together()
    {
        var root = Path.Combine(Path.GetTempPath(), "fo-paths-" + Guid.NewGuid().ToString("N"));

        var paths = new AppPaths(root);

        Assert.Equal(root, paths.VolatileRoot);
        Assert.Equal(Path.Combine(root, "tools", "steamcmd"), paths.SteamCmdDirectory);
    }

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
