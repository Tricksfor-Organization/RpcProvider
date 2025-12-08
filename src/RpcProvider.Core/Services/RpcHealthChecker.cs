using Microsoft.Extensions.Logging;
using RpcProvider.Core.Interfaces;
using RpcProvider.Core.Models;
using System.Net.Http.Json;
using System.Text.Json;

namespace RpcProvider.Core.Services;

/// <summary>
/// Service for checking the health of RPC endpoints.
/// </summary>
public class RpcHealthChecker
{
    private readonly IRpcRepository _repository;
    private readonly ILogger<RpcHealthChecker> _logger;
    private readonly HttpClient _httpClient;

    public RpcHealthChecker(
        IRpcRepository repository,
        IHttpClientFactory httpClientFactory,
        ILogger<RpcHealthChecker> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _httpClient = httpClientFactory?.CreateClient("RpcHealthCheck") 
            ?? throw new ArgumentNullException(nameof(httpClientFactory));
    }

    /// <summary>
    /// Checks the health of all Error state endpoints and updates their status if recovered.
    /// </summary>
    public async Task CheckErrorEndpointsAsync(CancellationToken cancellationToken = default)
    {
        var allEndpoints = await _repository.GetAllAsync(cancellationToken);
        var errorEndpoints = allEndpoints.Where(e => e.State == RpcState.Error).ToList();

        if (!errorEndpoints.Any())
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

            var isHealthy = await TestRpcEndpointAsync(endpoint.Url, cancellationToken);

            if (isHealthy)
            {
                endpoint.State = RpcState.Active;
                endpoint.ConsecutiveErrors = 0;
                endpoint.ErrorMessage = null;
                endpoint.UpdatedAt = DateTime.UtcNow;

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
    /// Tests an RPC endpoint by making a simple blockchain call.
    /// </summary>
    private async Task<bool> TestRpcEndpointAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            // Use eth_blockNumber as a simple test call - works for most EVM chains
            var request = new
            {
                jsonrpc = "2.0",
                method = "eth_blockNumber",
                @params = Array.Empty<string>(),
                id = 1
            };

            var response = await _httpClient.PostAsJsonAsync(url, request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogDebug("RPC health check failed for {Url}: HTTP {StatusCode}", 
                    url, response.StatusCode);
                return false;
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            var jsonDoc = JsonDocument.Parse(content);

            // Check if the response has a result
            if (jsonDoc.RootElement.TryGetProperty("result", out var result))
            {
                _logger.LogDebug("RPC health check successful for {Url}", url);
                return true;
            }

            // Check if there's an error in the response
            if (jsonDoc.RootElement.TryGetProperty("error", out var error))
            {
                _logger.LogDebug("RPC health check failed for {Url}: {Error}", 
                    url, error.GetProperty("message").GetString());
                return false;
            }

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
