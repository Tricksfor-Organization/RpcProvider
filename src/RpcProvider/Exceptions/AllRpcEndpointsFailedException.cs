using Nethereum.Signer;

namespace RpcProvider.Exceptions;

/// <summary>
/// Exception thrown when all available RPC endpoints have been tried and failed.
/// Contains details of all attempted endpoints and their failures.
/// </summary>
public class AllRpcEndpointsFailedException : RpcProviderException
{
    /// <summary>
    /// The blockchain chain for which all endpoints failed.
    /// </summary>
    public Chain Chain { get; }

    /// <summary>
    /// Collection of all attempted RPC URLs and their corresponding exceptions.
    /// </summary>
    public IReadOnlyList<RpcEndpointFailure> Failures { get; }

    public AllRpcEndpointsFailedException(Chain chain, IReadOnlyList<RpcEndpointFailure> failures)
        : base(BuildMessage(chain, failures))
    {
        Chain = chain;
        Failures = failures ?? throw new ArgumentNullException(nameof(failures));
    }

    public AllRpcEndpointsFailedException(Chain chain, IReadOnlyList<RpcEndpointFailure> failures, Exception innerException)
        : base(BuildMessage(chain, failures), innerException)
    {
        Chain = chain;
        Failures = failures ?? throw new ArgumentNullException(nameof(failures));
    }

    private static string BuildMessage(Chain chain, IReadOnlyList<RpcEndpointFailure> failures)
    {
        var failureCount = failures?.Count ?? 0;
        return $"All {failureCount} RPC endpoint(s) failed for chain: {chain} ({(int)chain})";
    }
}

/// <summary>
/// Represents a single RPC endpoint failure with its URL and exception.
/// </summary>
public record RpcEndpointFailure(string Url, Exception Exception);
