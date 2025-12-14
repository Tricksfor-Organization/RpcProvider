using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RpcProvider.Configuration;

namespace RpcProvider.Services;

/// <summary>
/// Background service that periodically checks the health of error-state RPC endpoints
/// and marks them as active if they have recovered.
/// </summary>
public class RpcHealthCheckWorker(
    IServiceProvider serviceProvider,
    ILogger<RpcHealthCheckWorker> logger,
    IOptions<RpcProviderOptions> options) : BackgroundService
{
    private readonly IServiceProvider _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
    private readonly ILogger<RpcHealthCheckWorker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    private readonly RpcProviderOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.EnableHealthChecks)
        {
            _logger.LogInformation("RPC health checks are disabled");
            return;
        }

        _logger.LogInformation("RPC Health Check Worker started. Checking every {Interval} minutes", 
            _options.HealthCheckIntervalMinutes);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PerformHealthChecksAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while performing health checks");
            }

            // Wait for the configured interval before next check
            await Task.Delay(TimeSpan.FromMinutes(_options.HealthCheckIntervalMinutes), stoppingToken);
        }

        _logger.LogInformation("RPC Health Check Worker stopped");
    }

    private async Task PerformHealthChecksAsync(CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var healthChecker = scope.ServiceProvider.GetRequiredService<RpcHealthChecker>();

        _logger.LogDebug("Starting RPC health check cycle");

        await healthChecker.CheckErrorEndpointsAsync(cancellationToken);

        _logger.LogDebug("Completed RPC health check cycle");
    }
}
