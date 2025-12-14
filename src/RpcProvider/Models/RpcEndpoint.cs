using Nethereum.Signer;

namespace RpcProvider.Models;

/// <summary>
/// Represents an RPC endpoint for blockchain interaction.
/// </summary>
public class RpcEndpoint
{
    /// <summary>
    /// Unique identifier for the RPC endpoint.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Chain represented as Nethereum Chain enum value.
    /// </summary>
    public Chain Chain { get; set; }

    /// <summary>
    /// The RPC endpoint URL.
    /// </summary>
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Current state of the RPC endpoint.
    /// </summary>
    public RpcState State { get; set; }

    /// <summary>
    /// Priority for endpoint selection (lower value = higher priority).
    /// </summary>
    public int Priority { get; set; }

    /// <summary>
    /// Number of consecutive errors encountered.
    /// </summary>
    public int ConsecutiveErrors { get; set; }

    /// <summary>
    /// Last error message if any.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Timestamp of the last error.
    /// </summary>
    public DateTime? LastErrorAt { get; set; }

    /// <summary>
    /// Timestamp when the record was created.
    /// </summary>
    public DateTime Created { get; set; }

    /// <summary>
    /// Timestamp when the record was last updated.
    /// </summary>
    public DateTime Modified { get; set; }
}
