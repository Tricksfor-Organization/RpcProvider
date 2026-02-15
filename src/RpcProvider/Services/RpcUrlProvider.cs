using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Signer;
using RpcProvider.Configuration;
using RpcProvider.Exceptions;
using RpcProvider.Interfaces;
using RpcProvider.Models;

namespace RpcProvider.Services;

/// <summary>
/// Main service for managing and retrieving RPC URLs with caching and failover support.
/// </summary>
public class RpcUrlProvider(
    IRpcRepository repository,
    HybridCache cache,
    IOptions<RpcProviderOptions> options,
    ILogger<RpcUrlProvider> logger) : IRpcUrlProvider
{
    private readonly IRpcRepository _repository = repository ?? throw new ArgumentNullException(nameof(repository));
    private readonly HybridCache _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    private readonly RpcProviderOptions _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
    private readonly ILogger<RpcUrlProvider> _logger = logger ?? throw new ArgumentNullException(nameof(logger));

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

        if (endpoints.Count == 0 && _options.AllowDisabledEndpointsAsFallback)
        {
            // 4. Last resort: Try disabled endpoints (emergency mode)
            _logger.LogCritical("No healthy RPC endpoints for chain {Chain} ({ChainId}), using disabled endpoints as fallback", 
                chain, (int)chain);
            endpoints = (await _repository.GetByChainAndStateAsync(chain, RpcState.Disabled, cancellationToken)).ToList();
        }

        if (endpoints.Count == 0)
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

        var nextEndpoint = endpoints[0];
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
        endpoint.Modified = DateTime.UtcNow;

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
        endpoint.Modified = DateTime.UtcNow;

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

    public async Task<TResult> ExecuteWithRetryAsync<TResult>(
        Chain chain,
        Func<string, CancellationToken, Task<TResult>> operation,
        bool ignoreBackoff = false,
        CancellationToken cancellationToken = default)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));

        var failures = new List<RpcEndpointFailure>();
        var triedUrls = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var attemptCount = 0;

        _logger.LogDebug("Starting ExecuteWithRetryAsync for chain {Chain} ({ChainId}), ignoreBackoff={IgnoreBackoff}", 
            chain, (int)chain, ignoreBackoff);

        // Get all available endpoints in priority order
        var availableEndpoints = await GetAvailableEndpointsForRetryAsync(chain, ignoreBackoff, cancellationToken);

        foreach (var endpoint in availableEndpoints)
        {
            // Check if we've already tried this URL
            if (triedUrls.Contains(endpoint.Url))
                continue;

            // Check max retry attempts limit
            if (_options.MaxRetryAttempts > 0 && attemptCount >= _options.MaxRetryAttempts)
            {
                _logger.LogWarning("Reached maximum retry attempts ({MaxRetryAttempts}) for chain {Chain} ({ChainId})", 
                    _options.MaxRetryAttempts, chain, (int)chain);
                break;
            }

            triedUrls.Add(endpoint.Url);
            attemptCount++;

            _logger.LogDebug("Attempting operation with RPC endpoint {Url} (attempt {Attempt}) for chain {Chain} ({ChainId})", 
                endpoint.Url, attemptCount, chain, (int)chain);

            try
            {
                var result = await operation(endpoint.Url, cancellationToken);
                
                // Success! Mark endpoint as successful
                await MarkAsSuccessAsync(endpoint.Url, cancellationToken);
                
                _logger.LogInformation("Successfully executed operation with RPC endpoint {Url} for chain {Chain} ({ChainId})", 
                    endpoint.Url, chain, (int)chain);
                
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Operation failed with RPC endpoint {Url} for chain {Chain} ({ChainId}): {ErrorMessage}", 
                    endpoint.Url, chain, (int)chain, ex.Message);

                // Mark endpoint as failed
                await MarkAsFailedAsync(endpoint.Url, ex, cancellationToken);
                
                // Track the failure
                failures.Add(new RpcEndpointFailure(endpoint.Url, ex));
            }
        }

        // All endpoints failed
        _logger.LogError("All {FailureCount} RPC endpoint(s) failed for chain {Chain} ({ChainId})", 
            failures.Count, chain, (int)chain);
        
        throw new AllRpcEndpointsFailedException(chain, failures);
    }

    public async Task ExecuteWithRetryAsync(
        Chain chain,
        Func<string, CancellationToken, Task> operation,
        bool ignoreBackoff = false,
        CancellationToken cancellationToken = default)
    {
        if (operation == null)
            throw new ArgumentNullException(nameof(operation));

        // Wrap void operation in a Func that returns a dummy result
        await ExecuteWithRetryAsync(
            chain,
            async (url, ct) =>
            {
                await operation(url, ct);
                return true; // Dummy return value
            },
            ignoreBackoff,
            cancellationToken);
    }

    #region Private Methods

    private async Task<List<RpcEndpoint>> GetAvailableEndpointsForRetryAsync(
        Chain chain, 
        bool ignoreBackoff, 
        CancellationToken cancellationToken)
    {
        var availableEndpoints = new List<RpcEndpoint>();

        // 1. Get all Active endpoints (highest priority)
        var activeEndpoints = await _repository.GetByChainAndStateAsync(chain, RpcState.Active, cancellationToken);
        availableEndpoints.AddRange(activeEndpoints);

        _logger.LogDebug("Found {Count} active endpoints for chain {Chain} ({ChainId})", 
            activeEndpoints.Count(), chain, (int)chain);

        // 2. Get Error endpoints
        var errorEndpoints = await _repository.GetByChainAndStateAsync(chain, RpcState.Error, cancellationToken);
        var errorEndpointsList = errorEndpoints.ToList();

        if (ignoreBackoff)
        {
            // Add all error endpoints regardless of backoff
            availableEndpoints.AddRange(errorEndpointsList);
            _logger.LogDebug("Added {Count} error endpoints (ignoring backoff) for chain {Chain} ({ChainId})", 
                errorEndpointsList.Count, chain, (int)chain);
        }
        else
        {
            // Only add error endpoints that passed backoff period
            var recoverableEndpoints = errorEndpointsList.Where(e => IsRecoverable(e)).ToList();
            availableEndpoints.AddRange(recoverableEndpoints);
            _logger.LogDebug("Added {Count} recoverable error endpoints (respecting backoff) for chain {Chain} ({ChainId})", 
                recoverableEndpoints.Count, chain, (int)chain);
        }

        // 3. If allowed, get Disabled endpoints as last resort
        if (_options.AllowDisabledEndpointsAsFallback)
        {
            var disabledEndpoints = await _repository.GetByChainAndStateAsync(chain, RpcState.Disabled, cancellationToken);
            availableEndpoints.AddRange(disabledEndpoints);
            _logger.LogDebug("Added {Count} disabled endpoints as fallback for chain {Chain} ({ChainId})", 
                disabledEndpoints.Count(), chain, (int)chain);
        }

        // Sort by priority, then by consecutive errors
        return availableEndpoints
            .OrderBy(e => e.Priority)
            .ThenBy(e => e.ConsecutiveErrors)
            .ToList();
    }

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
            
            // Try to get from cache - returns null if not found
            var cachedUrl = await _cache.GetOrCreateAsync<string?>(
                cacheKey,
                cancel => ValueTask.FromResult<string?>(null), // Factory returns null = not in cache
                cancellationToken: cancellationToken
            );

            return cachedUrl;
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
            var cacheOptions = new HybridCacheEntryOptions
            {
                Expiration = TimeSpan.FromSeconds(_options.CacheDurationSeconds)
            };

            await _cache.SetAsync(cacheKey, url, cacheOptions, cancellationToken: cancellationToken);
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

    private string GetCacheKey(Chain chain)
    {
        var key = $"rpc:best:{(int)chain}";
        
        if (!string.IsNullOrWhiteSpace(_options.CacheKeyPrefix))
        {
            key = $"{key}:{_options.CacheKeyPrefix}";
        }
        
        return key;
    }

    #endregion
}
