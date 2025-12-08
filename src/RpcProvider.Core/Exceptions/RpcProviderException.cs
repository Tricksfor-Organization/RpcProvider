namespace RpcProvider.Core.Exceptions;

/// <summary>
/// Exception thrown when RPC provider operations fail.
/// </summary>
public class RpcProviderException : Exception
{
    public RpcProviderException(string message) 
        : base(message)
    {
    }

    public RpcProviderException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}
