using AbioticServerManager.Core.Config;
using AbioticServerManager.Core.Runtime;
using Microsoft.Extensions.DependencyInjection;

namespace AbioticServerManager.Core;

public static class ServiceCollectionExtensions
{
    /// <summary>Registers the pure (no-IO) domain services from the Core assembly.</summary>
    public static IServiceCollection AddOverseerCore(this IServiceCollection services)
    {
        services.AddSingleton<ILaunchArgumentBuilder, LaunchArgumentBuilder>();
        services.AddSingleton<IConfigValidator, ConfigValidator>();
        services.AddSingleton<IServerExecutableLocator, ServerExecutableLocator>();
        return services;
    }
}
