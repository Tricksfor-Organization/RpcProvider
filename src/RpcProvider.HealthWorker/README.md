# RpcProvider.HealthWorker

Background health check service for RpcProvider that automatically monitors and restores failed RPC endpoints.

## Features

- 🔍 **Automatic Health Checks**: Periodically tests failed endpoints using Nethereum.Web3
- 🔄 **Auto-Recovery**: Automatically restores recovered endpoints to active state
- ⏱️ **Exponential Backoff**: Smart retry timing to avoid overwhelming failed endpoints
- 🎯 **Configurable Intervals**: Adjust health check frequency to your needs
- 📊 **Error Reset**: Automatically resets consecutive error counts on recovery

## Installation

```bash
dotnet add package RpcProvider.HealthWorker
```

**Note:** This package requires `RpcProvider.Core` to be installed and configured.

## Quick Start

### 1. Add Health Worker Service

```csharp
using RpcProvider.HealthWorker;
using RpcProvider.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add RpcProvider.Core services first
builder.Services.AddRpcUrlProvider((options, services) =>
{
    options.CacheDurationSeconds = 300;
    options.MaxConsecutiveErrorsBeforeDisable = 5;
    options.HealthCheckIntervalMinutes = 5;
    options.EnableHealthChecks = true;
});

// Add your DbContext and repository
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));
builder.Services.AddScoped<IRpcRepository, RpcRepository>();

// Add the health check worker
builder.Services.AddRpcHealthCheckWorker();

var app = builder.Build();
app.Run();
```

### 2. Configure Options

In `appsettings.json`:

```json
{
  "RpcProvider": {
    "HealthCheckIntervalMinutes": 5,
    "EnableHealthChecks": true,
    "BaseBackoffMinutes": 1,
    "MaxBackoffMinutes": 30,
    "MaxConsecutiveErrorsBeforeDisable": 5
  }
}
```

## How It Works

The health worker runs as a background service (`IHostedService`) that:

1. **Runs Periodically**: Executes every N minutes (configurable via `HealthCheckIntervalMinutes`)
2. **Queries Failed Endpoints**: Gets all endpoints in `Error` state from the database
3. **Respects Exponential Backoff**: Only tests endpoints whose backoff period has elapsed
4. **Tests with Nethereum**: Calls `web3.Eth.Blocks.GetBlockNumber.SendRequestAsync()` to verify the endpoint
5. **Restores on Success**: Marks recovered endpoints as `Active` and resets error counts
6. **Logs Activity**: Provides detailed logging of health check operations

## Exponential Backoff Strategy

Failed endpoints use exponential backoff to prevent overwhelming them:

| Error Count | Backoff Time |
|-------------|--------------|
| 1           | 1 minute     |
| 2           | 2 minutes    |
| 3           | 4 minutes    |
| 4           | 8 minutes    |
| 5           | 16 minutes   |
| 6+          | 30 minutes   |

The backoff time is calculated as: `min(BaseBackoffMinutes * 2^(ConsecutiveErrors - 1), MaxBackoffMinutes)`

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `HealthCheckIntervalMinutes` | int | 5 | How often to run health checks |
| `EnableHealthChecks` | bool | true | Enable or disable the worker |
| `BaseBackoffMinutes` | int | 1 | Base time for exponential backoff |
| `MaxBackoffMinutes` | int | 30 | Maximum backoff time |

## Health Check Process

```
┌─────────────────────────────────────────────────────┐
│                  Health Worker                       │
├─────────────────────────────────────────────────────┤
│                                                      │
│  1. Timer triggers every N minutes                  │
│                                                      │
│  2. Query all endpoints with State = Error          │
│                                                      │
│  3. For each endpoint:                              │
│     • Calculate backoff time                        │
│     • If backoff elapsed:                           │
│       - Test using Nethereum Web3                   │
│       - If success:                                 │
│         * Set State = Active                        │
│         * Reset ConsecutiveErrors = 0               │
│         * Clear ErrorMessage                        │
│       - If failure:                                 │
│         * Keep State = Error                        │
│         * Update LastErrorAt                        │
│                                                      │
│  4. Log results and continue                        │
│                                                      │
└─────────────────────────────────────────────────────┘
```

## Logging

The worker provides detailed logs:

```
info: RpcProvider.HealthWorker[0]
      Starting RPC health check...
info: RpcProvider.HealthWorker[0]
      Found 3 endpoints in Error state
info: RpcProvider.HealthWorker[0]
      Testing endpoint: https://eth.example.com (Backoff elapsed: True)
info: RpcProvider.HealthWorker[0]
      Endpoint https://eth.example.com is now healthy. Marked as Active.
info: RpcProvider.HealthWorker[0]
      Health check completed. Recovered: 1, Still failed: 2
```

## Disabling the Worker

To temporarily disable health checks:

```json
{
  "RpcProvider": {
    "EnableHealthChecks": false
  }
}
```

Or remove the `AddRpcHealthCheckWorker()` registration.

## Best Practices

1. **Set Appropriate Intervals**: Balance between quick recovery and system load
   - High-traffic apps: 5-10 minutes
   - Low-traffic apps: 15-30 minutes

2. **Monitor Logs**: Watch for patterns in endpoint failures

3. **Adjust Backoff Settings**: Tune based on your RPC providers' recovery times

4. **Use with Core**: This package requires `RpcProvider.Core` to function

## Requirements

- .NET 10.0 or higher
- RpcProvider.Core
- Entity Framework Core
- Nethereum.Web3 5.0.0+

## Dependencies

This package automatically includes:
- `RpcProvider.Core` (as project reference)
- `Microsoft.Extensions.Hosting.Abstractions`

## Links

- [GitHub Repository](https://github.com/Tricksfor-Organization/RpcProvider)
- [RpcProvider.Core](https://www.nuget.org/packages/RpcProvider.Core)
- [Full Documentation](https://github.com/Tricksfor-Organization/RpcProvider/blob/main/README.md)

## License

This project is licensed under the MIT License.
