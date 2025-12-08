namespace RpcProvider.Core.Models;

/// <summary>
/// Represents the state of an RPC endpoint.
/// </summary>
public enum RpcState
{
    /// <summary>
    /// Endpoint is active and available for use.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Endpoint has encountered errors and is temporarily unavailable.
    /// </summary>
    Error = 1,

    /// <summary>
    /// Endpoint has been manually disabled.
    /// </summary>
    Disabled = 2
}
