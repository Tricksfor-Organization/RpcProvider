namespace RpcProvider.Configuration;

/// <summary>
/// Configuration options for the RPC provider.
/// </summary>
public class RpcProviderOptions
{
    /// <summary>
    /// Duration in seconds to cache healthy RPC endpoints.
    /// Default: 300 seconds (5 minutes).
    /// </summary>
    public int CacheDurationSeconds { get; set; } = 300;

    /// <summary>
    /// Maximum number of consecutive errors before an endpoint is marked as Error state.
    /// Default: 5.
    /// </summary>
    public int MaxConsecutiveErrorsBeforeDisable { get; set; } = 5;

    /// <summary>
    /// Request timeout in seconds for RPC calls.
    /// Default: 30 seconds.
    /// </summary>
    public int RequestTimeoutSeconds { get; set; } = 30;

    /// <summary>
    /// Whether to allow disabled endpoints as a last resort fallback.
    /// Default: false.
    /// </summary>
    public bool AllowDisabledEndpointsAsFallback { get; set; } = false;

    /// <summary>
    /// Interval in minutes for health check background service.
    /// Default: 5 minutes.
    /// </summary>
    public int HealthCheckIntervalMinutes { get; set; } = 5;

    /// <summary>
    /// Enable or disable health checks.
    /// Default: true.
    /// </summary>
    public bool EnableHealthChecks { get; set; } = true;

    /// <summary>
    /// Base backoff time in minutes for exponential backoff.
    /// Default: 1 minute.
    /// </summary>
    public int BaseBackoffMinutes { get; set; } = 1;

    /// <summary>
    /// Maximum backoff time in minutes for exponential backoff.
    /// Default: 30 minutes.
    /// </summary>
    public int MaxBackoffMinutes { get; set; } = 30;
}
