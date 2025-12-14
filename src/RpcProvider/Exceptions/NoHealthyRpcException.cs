using Nethereum.Signer;

namespace RpcProvider.Exceptions;

/// <summary>
/// Exception thrown when no healthy RPC endpoints are available for a chain.
/// </summary>
public class NoHealthyRpcException : RpcProviderException
{
    public Chain Chain { get; }

    public NoHealthyRpcException(Chain chain) 
        : base($"No healthy RPC endpoints available for chain: {chain} ({(int)chain})")
    {
        Chain = chain;
    }

    public NoHealthyRpcException(Chain chain, string message) 
        : base(message)
    {
        Chain = chain;
    }

    public NoHealthyRpcException(Chain chain, string message, Exception innerException) 
        : base(message, innerException)
    {
        Chain = chain;
    }
}
