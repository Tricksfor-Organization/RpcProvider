using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Signer;
using RpcProvider.Core.Configuration;
using RpcProvider.Core.Exceptions;
using RpcProvider.Core.Interfaces;
using RpcProvider.Core.Models;
using System.Text;

namespace RpcProvider.Core.Services;

/// <summary>
/// Main service for managing and retrieving RPC URLs with caching and failover support.
/// </summary>
public class RpcUrlProvider : IRpcUrlProvider
{
    private readonly IRpcRepository _repository;
    private readonly IDistributedCache _cache;
    private readonly RpcProviderOptions _options;
    private readonly ILogger<RpcUrlProvider> _logger;

    public RpcUrlProvider(
        IRpcRepository repository,
        IDistributedCache cache,
        IOptions<RpcProviderOptions> options,
        ILogger<RpcUrlProvider> logger)
    {
        _repository = repository ?? throw new ArgumentNullException(nameof(repository));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<string> GetBestRpcUrlAsync(Chain chain, CancellationToken cancellationToken = default)
    {
        // 1. Try to get from cache first
        var cachedUrl = await GetFromCacheAsync(chain, cancellationToken);
        if (!string.IsNullOrEmpty(cachedUrl))
        {
            _logger.LogDebug("Retrieved cached RPC URL for chain {Chain} ({ChainId})", chain, (int)chain);
            return cachedUrl;
        }

        // 2. Get Active endpoints first
        var endpoints = (await _repository.GetByChainAndStateAsync(chain, RpcState.Active, cancellationToken)).ToList();

        if (!endpoints.Any())
        {
            // 3. Fallback: Try Error state endpoints (with exponential backoff check)
            _logger.LogWarning("No active RPC endpoints for chain {Chain} ({ChainId}), attempting error state endpoints", 
                chain, (int)chain);
            endpoints = (await GetRecoverableErrorEndpointsAsync(chain, cancellationToken)).ToList();
        }

        if (!endpoints.Any())
        {
            // 4. Last resort: Try disabled endpoints (emergency mode)
            if (_options.AllowDisabledEndpointsAsFallback)
            {
                _logger.LogCritical("No healthy RPC endpoints for chain {Chain} ({ChainId}), using disabled endpoints as fallback", 
                    chain, (int)chain);
                endpoints = (await _repository.GetByChainAndStateAsync(chain, RpcState.Disabled, cancellationToken)).ToList();
            }
        }

        if (!endpoints.Any())
        {
            _logger.LogError("No available RPC endpoints for chain {Chain} ({ChainId})", chain, (int)chain);
            throw new NoHealthyRpcException(chain);
        }

        // 5. Select best endpoint (Priority ASC, ConsecutiveErrors ASC)
        var bestEndpoint = endpoints
            .OrderBy(e => e.Priority)
            .ThenBy(e => e.ConsecutiveErrors)
            .First();

        _logger.LogInformation("Selected RPC endpoint {Url} (Priority: {Priority}) for chain {Chain} ({ChainId})", 
            bestEndpoint.Url, bestEndpoint.Priority, chain, (int)chain);

        // 6. Cache the result
        await CacheEndpointAsync(chain, bestEndpoint.Url, cancellationToken);

        return bestEndpoint.Url;
    }

    public async Task<string> GetNextRpcUrlAsync(Chain chain, string failedUrl, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(failedUrl))
            throw new ArgumentException("Failed URL cannot be null or empty", nameof(failedUrl));

        _logger.LogDebug("Getting next RPC URL for chain {Chain} ({ChainId}), excluding {FailedUrl}", 
            chain, (int)chain, failedUrl);

        // Get all endpoints for the chain
        var endpoints = (await _repository.GetByChainAsync(chain, cancellationToken))
            .Where(e => e.Url != failedUrl && (e.State == RpcState.Active || 
                   (e.State == RpcState.Error && IsRecoverable(e))))
            .OrderBy(e => e.Priority)
            .ThenBy(e => e.ConsecutiveErrors)
            .ToList();

        if (!endpoints.Any())
        {
            _logger.LogError("No alternative RPC endpoints available for chain {Chain} ({ChainId})", 
                chain, (int)chain);
            throw new NoHealthyRpcException(chain, $"No alternative RPC endpoints available for chain: {chain} ({(int)chain})");
        }

        var nextEndpoint = endpoints.First();
        _logger.LogInformation("Selected next RPC endpoint {Url} for chain {Chain} ({ChainId})", 
            nextEndpoint.Url, chain, (int)chain);

        // Update cache with new endpoint
        await CacheEndpointAsync(chain, nextEndpoint.Url, cancellationToken);

        return nextEndpoint.Url;
    }

    public async Task MarkAsFailedAsync(string url, Exception exception, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        var endpoint = await _repository.GetByUrlAsync(url, cancellationToken);
        if (endpoint == null)
        {
            _logger.LogWarning("Attempted to mark non-existent RPC endpoint as failed: {Url}", url);
            return;
        }

        endpoint.ConsecutiveErrors++;
        endpoint.LastErrorAt = DateTime.UtcNow;
        endpoint.ErrorMessage = exception?.Message ?? "Unknown error";
        endpoint.UpdatedAt = DateTime.UtcNow;

        // Mark as Error if threshold exceeded
        if (endpoint.ConsecutiveErrors >= _options.MaxConsecutiveErrorsBeforeDisable)
        {
            endpoint.State = RpcState.Error;
            _logger.LogWarning("RPC endpoint {Url} marked as Error after {Count} consecutive errors", 
                url, endpoint.ConsecutiveErrors);
        }
        else
        {
            _logger.LogDebug("RPC endpoint {Url} error count increased to {Count}", url, endpoint.ConsecutiveErrors);
        }

        await _repository.UpdateAsync(endpoint, cancellationToken);

        // Invalidate cache for this chain
        await InvalidateCacheAsync(endpoint.Chain, cancellationToken);
    }

    public async Task MarkAsSuccessAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be null or empty", nameof(url));

        var endpoint = await _repository.GetByUrlAsync(url, cancellationToken);
        if (endpoint == null)
        {
            _logger.LogWarning("Attempted to mark non-existent RPC endpoint as successful: {Url}", url);
            return;
        }

        var wasInErrorState = endpoint.State == RpcState.Error || endpoint.ConsecutiveErrors > 0;

        endpoint.ConsecutiveErrors = 0;
        endpoint.ErrorMessage = null;
        endpoint.UpdatedAt = DateTime.UtcNow;

        // Mark as Active if it was in Error state
        if (endpoint.State == RpcState.Error)
        {
            endpoint.State = RpcState.Active;
            _logger.LogInformation("RPC endpoint {Url} recovered and marked as Active", url);
        }

        await _repository.UpdateAsync(endpoint, cancellationToken);

        if (wasInErrorState)
        {
            // Invalidate cache to pick up the recovered endpoint
            await InvalidateCacheAsync(endpoint.Chain, cancellationToken);
        }
    }

    public async Task<IEnumerable<RpcEndpoint>> GetEndpointsByChainAsync(Chain chain, CancellationToken cancellationToken = default)
    {
        return await _repository.GetByChainAsync(chain, cancellationToken);
    }

    public async Task<bool> IsHealthyAsync(string url, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return false;

        var endpoint = await _repository.GetByUrlAsync(url, cancellationToken);
        return endpoint?.State == RpcState.Active && endpoint.ConsecutiveErrors == 0;
    }

    #region Private Methods

    private async Task<IEnumerable<RpcEndpoint>> GetRecoverableErrorEndpointsAsync(Chain chain, CancellationToken cancellationToken)
    {
        var errorEndpoints = await _repository.GetByChainAndStateAsync(chain, RpcState.Error, cancellationToken);

        // Only return endpoints that passed exponential backoff period
        return errorEndpoints.Where(e => IsRecoverable(e));
    }

    private bool IsRecoverable(RpcEndpoint endpoint)
    {
        if (endpoint.LastErrorAt == null)
            return true;

        var backoffTime = CalculateBackoff(endpoint.ConsecutiveErrors);
        var timeSinceError = DateTime.UtcNow - endpoint.LastErrorAt.Value;

        return timeSinceError >= backoffTime;
    }

    private TimeSpan CalculateBackoff(int consecutiveErrors)
    {
        // Exponential backoff: baseMinutes * 2^(errors-1), capped at maxMinutes
        var minutes = _options.BaseBackoffMinutes * Math.Pow(2, consecutiveErrors - 1);
        minutes = Math.Min(minutes, _options.MaxBackoffMinutes);

        return TimeSpan.FromMinutes(minutes);
    }

    private async Task<string?> GetFromCacheAsync(Chain chain, CancellationToken cancellationToken)
    {
        try
        {
            var cacheKey = GetCacheKey(chain);
            var cachedBytes = await _cache.GetAsync(cacheKey, cancellationToken);

            if (cachedBytes != null && cachedBytes.Length > 0)
            {
                return Encoding.UTF8.GetString(cachedBytes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve from cache for chain {Chain} ({ChainId})", 
                chain, (int)chain);
        }

        return null;
    }

    private async Task CacheEndpointAsync(Chain chain, string url, CancellationToken cancellationToken)
    {
        try
        {
            var cacheKey = GetCacheKey(chain);
            var cacheBytes = Encoding.UTF8.GetBytes(url);
            var options = new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(_options.CacheDurationSeconds)
            };

            await _cache.SetAsync(cacheKey, cacheBytes, options, cancellationToken);
            _logger.LogDebug("Cached RPC URL for chain {Chain} ({ChainId})", chain, (int)chain);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache RPC URL for chain {Chain} ({ChainId})", 
                chain, (int)chain);
        }
    }

    private async Task InvalidateCacheAsync(Chain chain, CancellationToken cancellationToken)
    {
        try
        {
            var cacheKey = GetCacheKey(chain);
            await _cache.RemoveAsync(cacheKey, cancellationToken);
            _logger.LogDebug("Invalidated cache for chain {Chain} ({ChainId})", chain, (int)chain);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to invalidate cache for chain {Chain} ({ChainId})", 
                chain, (int)chain);
        }
    }

    private string GetCacheKey(Chain chain) => $"rpc:best:{(int)chain}";

    #endregion
}
