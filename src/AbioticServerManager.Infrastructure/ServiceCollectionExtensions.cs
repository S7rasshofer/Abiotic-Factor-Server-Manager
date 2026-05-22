using AbioticServerManager.Core.Admin;
using AbioticServerManager.Core.Backup;
using AbioticServerManager.Core.Diagnostics;
using AbioticServerManager.Core.Install;
using AbioticServerManager.Core.Networking;
using AbioticServerManager.Core.Runtime;
using AbioticServerManager.Core.Schema;
using AbioticServerManager.Core.Services;
using AbioticServerManager.Core.Worlds;
using AbioticServerManager.Infrastructure.FileSystem;
using AbioticServerManager.Infrastructure.Install;
using AbioticServerManager.Infrastructure.Migration;
using AbioticServerManager.Infrastructure.Networking;
using AbioticServerManager.Infrastructure.Persistence;
using AbioticServerManager.Infrastructure.Process;
using AbioticServerManager.Infrastructure.SteamCmd;
using Microsoft.Extensions.DependencyInjection;

namespace AbioticServerManager.Infrastructure;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers IO-bound infrastructure services (persistence, SteamCMD, process).</summary>
    public static IServiceCollection AddOverseerInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<IInstanceStore, JsonInstanceStore>();
        services.AddSingleton<IPlayerRosterStore, JsonPlayerRosterStore>();
        services.AddSingleton<IInternalIpSnapshotStore, JsonInternalIpSnapshotStore>();
        services.AddSingleton<IPublicIpProbe, HttpPublicIpProbe>();
        services.AddSingleton<IResetManagedDataService, ResetManagedDataService>();
        services.AddSingleton<IWorldIdentityMigrationService, WorldIdentityMigrationService>();
        services.AddSingleton<ILegacyMigrationService, LegacyMigrationService>();
        services.AddSingleton<IBackupService, FileBackupService>();
        services.AddSingleton<IAdminListService, AdminListService>();
        services.AddSingleton<IPlayerBanService, PlayerBanService>();
        services.AddSingleton<ISteamCmdService, SteamCmdService>();
        services.AddSingleton<IServerInstallStateService, ServerInstallStateService>();
        services.AddSingleton<IWorldIntegrityInspector, WorldIntegrityInspector>();
        services.AddSingleton<IServerProcessService, ServerProcessService>();
        services.AddSingleton<A2SQueryClient>();
        services.AddSingleton<IDiagnosticsService, DiagnosticsService>();
        services.AddSingleton<INetworkSetupService, WindowsNetworkSetupService>();
        services.AddSingleton<ISchemaCache, JsonSchemaCache>();
        services.AddSingleton<ISettingMetadataCatalog>(sp =>
        {
            var paths = sp.GetRequiredService<IAppPaths>();
            var overridePath = System.IO.Path.Combine(paths.ConfigDirectory, "setting-metadata.json");
            return new SettingMetadataCatalog(overridePath);
        });
        services.AddSingleton<ISandboxSettingsService, SandboxSettingsService>();
        return services;
    }
}
