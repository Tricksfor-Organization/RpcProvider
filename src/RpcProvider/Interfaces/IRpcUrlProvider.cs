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

    /// <summary>
    /// Executes an operation with automatic retry across all available RPC endpoints.
    /// Tries endpoints in order: Active → Error (backoff passed) → Error (all if ignoreBackoff) → Disabled (if configured).
    /// Marks endpoints as failed/successful automatically.
    /// </summary>
    /// <typeparam name="TResult">The type of result returned by the operation.</typeparam>
    /// <param name="chain">The blockchain chain to execute the operation on.</param>
    /// <param name="operation">The operation to execute. Receives the RPC URL and cancellation token.</param>
    /// <param name="ignoreBackoff">If true, ignores exponential backoff and tries all Error state endpoints immediately. Default: false.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The result of the operation.</returns>
    /// <exception cref="AllRpcEndpointsFailedException">Thrown when all available endpoints have been tried and failed.</exception>
    Task<TResult> ExecuteWithRetryAsync<TResult>(
        Chain chain,
        Func<string, CancellationToken, Task<TResult>> operation,
        bool ignoreBackoff = false,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes an operation with automatic retry across all available RPC endpoints.
    /// Tries endpoints in order: Active → Error (backoff passed) → Error (all if ignoreBackoff) → Disabled (if configured).
    /// Marks endpoints as failed/successful automatically.
    /// </summary>
    /// <param name="chain">The blockchain chain to execute the operation on.</param>
    /// <param name="operation">The operation to execute. Receives the RPC URL and cancellation token.</param>
    /// <param name="ignoreBackoff">If true, ignores exponential backoff and tries all Error state endpoints immediately. Default: false.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="AllRpcEndpointsFailedException">Thrown when all available endpoints have been tried and failed.</exception>
    Task ExecuteWithRetryAsync(
        Chain chain,
        Func<string, CancellationToken, Task> operation,
        bool ignoreBackoff = false,
        CancellationToken cancellationToken = default);
}
