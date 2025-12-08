using Microsoft.Extensions.DependencyInjection;

namespace RpcProvider.HealthWorker;

/// <summary>
/// Extension methods for registering the RPC health check worker.
/// </summary>
public static class HealthWorkerExtensions
{
    /// <summary>
    /// Adds the RPC health check background worker to the service collection.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddRpcHealthCheckWorker(this IServiceCollection services)
    {
        services.AddHostedService<RpcHealthCheckWorker>();
        return services;
    }
}
