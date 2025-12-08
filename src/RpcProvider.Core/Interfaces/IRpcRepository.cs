using Nethereum.Signer;
using RpcProvider.Core.Models;

namespace RpcProvider.Core.Interfaces;

/// <summary>
/// Repository interface for managing RPC endpoints in the database.
/// Each project implements this interface using their own DbContext.
/// </summary>
public interface IRpcRepository
{
    /// <summary>
    /// Gets all RPC endpoints for a specific chain and state.
    /// </summary>
    Task<IEnumerable<RpcEndpoint>> GetByChainAndStateAsync(
        Chain chain, 
        RpcState state, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all RPC endpoints for a specific chain.
    /// </summary>
    Task<IEnumerable<RpcEndpoint>> GetByChainAsync(
        Chain chain, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an RPC endpoint by its URL.
    /// </summary>
    Task<RpcEndpoint?> GetByUrlAsync(
        string url, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets an RPC endpoint by its ID.
    /// </summary>
    Task<RpcEndpoint?> GetByIdAsync(
        Guid id, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates an existing RPC endpoint.
    /// </summary>
    Task UpdateAsync(
        RpcEndpoint endpoint, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds a new RPC endpoint.
    /// </summary>
    Task AddAsync(
        RpcEndpoint endpoint, 
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all RPC endpoints.
    /// </summary>
    Task<IEnumerable<RpcEndpoint>> GetAllAsync(
        CancellationToken cancellationToken = default);
}
