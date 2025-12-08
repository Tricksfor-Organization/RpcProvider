using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RpcProvider.Core.Configuration;
using RpcProvider.Core.Interfaces;
using RpcProvider.Core.Services;

namespace RpcProvider.Core.Extensions;

/// <summary>
/// Extension methods for registering RPC provider services.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds RPC URL provider services to the service collection.
    /// Note: IRpcRepository must be registered separately by each project.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration.</param>
    /// <param name="sectionName">The configuration section name. Default is "RpcProvider".</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRpcUrlProvider(
        this IServiceCollection services,
        IConfiguration configuration,
        string sectionName = "RpcProvider")
    {
        // Register configuration options
        services.Configure<RpcProviderOptions>(configuration.GetSection(sectionName));

        // Register services
        services.AddScoped<IRpcUrlProvider, RpcUrlProvider>();
        services.AddScoped<RpcHealthChecker>();

        // Register HTTP client for health checks
        services.AddHttpClient("RpcHealthCheck", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }

    /// <summary>
    /// Adds RPC URL provider services with custom options configuration.
    /// Note: IRpcRepository must be registered separately by each project.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureOptions">Action to configure options with access to the service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRpcUrlProvider(
        this IServiceCollection services,
        Action<RpcProviderOptions, IServiceCollection> configureOptions)
    {
        // Register configuration options with access to service collection
        services.Configure<RpcProviderOptions>(options => configureOptions(options, services));

        // Register services
        services.AddScoped<IRpcUrlProvider, RpcUrlProvider>();
        services.AddScoped<RpcHealthChecker>();

        // Register HTTP client for health checks
        services.AddHttpClient("RpcHealthCheck", client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
        });

        return services;
    }
}
