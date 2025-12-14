using Nethereum.Signer;
using RpcProvider.Models;

namespace RpcProvider.Interfaces;

/// <summary>
/// Main service interface for managing and retrieving RPC URLs.
/// </summary>
public interface IRpcUrlProvider
{
    /// <summary>
    /// Gets the best available RPC URL for the specified chain.
    /// Throws NoHealthyRpcException if no healthy endpoints are available.
    /// </summary>
    Task<string> GetBestRpcUrlAsync(
        Chain chain, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets the next available RPC URL, excluding the failed one.
    /// Used for automatic retry with a different endpoint.
    /// </summary>
    Task<string> GetNextRpcUrlAsync(
        Chain chain, 
        string failedUrl, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an RPC URL as failed and increments error count.
    /// </summary>
    Task MarkAsFailedAsync(
        string url, 
        Exception exception, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Marks an RPC URL as successful and resets error count.
    /// </summary>
    Task MarkAsSuccessAsync(
        string url, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all RPC endpoints for a specific chain.
    /// </summary>
    Task<IEnumerable<RpcEndpoint>> GetEndpointsByChainAsync(
        Chain chain, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Tests if an RPC endpoint is healthy by making a test call.
    /// </summary>
    Task<bool> IsHealthyAsync(
        string url, 
        CancellationToken cancellationToken = default);
}
