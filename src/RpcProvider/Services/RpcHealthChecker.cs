using Microsoft.Extensions.Logging;
using Nethereum.Web3;
using RpcProvider.Interfaces;
using RpcProvider.Models;

namespace RpcProvider.Services;

/// <summary>
/// Service for checking the health of RPC endpoints.
/// </summary>
public class RpcHealthChecker(
    IRpcRepository repository,
    ILogger<RpcHealthChecker> logger)
{
    private readonly IRpcRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly ILogger<RpcHealthChecker> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

    /// <summary>
    /// Checks the health of all Error state endpoints and updates their status if recovered.
    /// </summary>
    public async Task CheckErrorEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var allEndpoints = await _repository.GetAllAsync(cancellationToken);
        var errorEndpoints = allEndpoints.Where(e => e.State == RpcState.Error).ToList();

        if (errorEndpoints.Count == 0)
        {
            _logger.LogDebug("No error state endpoints to check");
            return;
        }

        _logger.LogInformation("Checking {Count} error state RPC endpoints", errorEndpoints.Count);

        var checkTasks = errorEndpoints.Select(endpoint => CheckEndpointAsync(endpoint, cancellationToken));
        await Task.WhenAll(checkTasks);
    }

    /// <summary>
    /// Checks a specific endpoint's health.
    /// </summary>
    public async Task<bool> CheckEndpointAsync(RpcEndpoint endpoint, CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogDebug("Checking health of RPC endpoint {Url}", endpoint.Url);

            var isHealthy = await TestRpcEndpointAsync(endpoint.Url);

            if (isHealthy)
            {
                endpoint.State = RpcState.Active;
                endpoint.ConsecutiveErrors = 0;
                endpoint.ErrorMessage = null;
                endpoint.Modified = DateTime.UtcNow;

                await _repository.UpdateAsync(endpoint, cancellationToken);

                _logger.LogInformation("RPC endpoint {Url} recovered and marked as Active", endpoint.Url);
                return true;
            }
            else
            {
                _logger.LogDebug("RPC endpoint {Url} still unhealthy", endpoint.Url);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check health of RPC endpoint {Url}", endpoint.Url);
            return false;
        }
    }

    /// <summary>
    /// Tests an RPC endpoint by making a simple blockchain call using Nethereum.Web3.
    /// </summary>
    private async Task<bool> TestRpcEndpointAsync(string url)
    {
        try
        {
            // Use Nethereum.Web3 to get the block number - works for most EVM chains
            var web3 = new Web3(url);
            var blockNumber = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

            if (blockNumber != null && blockNumber.Value >= 0)
            {
                _logger.LogDebug("RPC health check successful for {Url}, current block: {BlockNumber}", 
                    url, blockNumber.Value);
                return true;
            }

            _logger.LogDebug("RPC health check failed for {Url}: Invalid block number response", url);
            return false;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogDebug(ex, "HTTP request failed for RPC endpoint {Url}", url);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            _logger.LogDebug(ex, "Request timeout for RPC endpoint {Url}", url);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Unexpected error testing RPC endpoint {Url}", url);
            return false;
        }
    }
}
