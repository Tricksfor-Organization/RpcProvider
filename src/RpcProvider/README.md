# RpcProvider.Core

A robust .NET library for managing blockchain RPC endpoints with automatic failover, intelligent health monitoring, and distributed caching.

## Features

- 🔄 **Automatic Failover**: Seamlessly switch between RPC endpoints when failures occur
- 🏥 **Health Monitoring**: Built-in health checks using Nethereum.Web3
- 🚀 **Intelligent Selection**: Priority-based endpoint selection with exponential backoff
- 💾 **HybridCache**: In-memory + distributed (Redis) caching with automatic fallback
- 📊 **State Management**: Track endpoint states (Active, Error, Disabled)
- 🔁 **Retry Logic**: Automatic retry with different endpoints

## Installation

```bash
dotnet add package RpcProvider.Core
```

## Quick Start

### 1. Configure Services

```csharp
using RpcProvider.Core.Extensions;

var builder = WebApplication.CreateBuilder(args);

// Add RpcProvider services
builder.Services.AddRpcUrlProvider((options, services) =>
{
    options.CacheDurationSeconds = 300;
    options.MaxConsecutiveErrorsBeforeDisable = 5;
    options.RequestTimeoutSeconds = 30;
});

// Add your DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(connectionString));

// Implement and register IRpcRepository
builder.Services.AddScoped<IRpcRepository, RpcRepository>();

// Optional: Add Redis for distributed caching
// If not configured, HybridCache uses in-memory caching only
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379";
});
// OR use Aspire:
// builder.AddRedisDistributedCache("cache");
```

### 2. Configure Entity Framework

```csharp
using Microsoft.EntityFrameworkCore;
using Nethereum.Signer;
using RpcProvider.Core.Models;

public class ApplicationDbContext : DbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<RpcEndpoint>(entity =>
        {
            entity.HasKey(e => e.Id);
            
            // Store Chain enum as long
            entity.Property(e => e.Chain)
                .HasConversion<long>();
            
            entity.Property(e => e.Url)
                .HasMaxLength(500)
                .IsRequired();
            
            entity.HasIndex(e => new { e.Chain, e.State, e.Priority });
        });
    }
}
```

### 3. Use in Your Service

```csharp
using Nethereum.Signer;
using Nethereum.Web3;
using RpcProvider.Core.Interfaces;

public class BlockchainService
{
    private readonly IRpcUrlProvider _rpcProvider;

    public BlockchainService(IRpcUrlProvider rpcProvider)
    {
        _rpcProvider = rpcProvider;
    }

    public async Task<string> GetBalanceAsync(string address, Chain chain)
    {
        string rpcUrl = await _rpcProvider.GetBestRpcUrlAsync(chain);

        try
        {
            var web3 = new Web3(rpcUrl);
            var balance = await web3.Eth.GetBalance.SendRequestAsync(address);

            // Always mark success
            await _rpcProvider.MarkAsSuccessAsync(rpcUrl);

            return Web3.Convert.FromWei(balance.Value).ToString();
        }
        catch (Exception ex)
        {
            // Always mark failure
            await _rpcProvider.MarkAsFailedAsync(rpcUrl, ex);
            throw;
        }
    }
}
```

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `CacheDurationSeconds` | int | 300 | Duration to cache healthy RPC endpoints |
| `MaxConsecutiveErrorsBeforeDisable` | int | 5 | Max errors before marking endpoint as Error |
| `RequestTimeoutSeconds` | int | 30 | Request timeout for RPC calls |
| `AllowDisabledEndpointsAsFallback` | bool | false | Use disabled endpoints as last resort |
| `BaseBackoffMinutes` | int | 1 | Base backoff time for exponential backoff |
| `MaxBackoffMinutes` | int | 30 | Maximum backoff time |

## Seeding Initial Data

```csharp
using Nethereum.Signer;
using RpcProvider.Core.Models;

var endpoints = new List<RpcEndpoint>
{
    new RpcEndpoint
    {
        Id = Guid.NewGuid(),
        Chain = Chain.MainNet, // Ethereum Mainnet
        Url = "https://eth-mainnet.g.alchemy.com/v2/YOUR_API_KEY",
        State = RpcState.Active,
        Priority = 1,
        Created = DateTime.UtcNow,
        Modified = DateTime.UtcNow
    },
    new RpcEndpoint
    {
        Id = Guid.NewGuid(),
        Chain = Chain.Polygon, // Polygon
        Url = "https://polygon-rpc.com",
        State = RpcState.Active,
        Priority = 1,
        Created = DateTime.UtcNow,
        Modified = DateTime.UtcNow
    }
};

await context.RpcEndpoints.AddRangeAsync(endpoints);
await context.SaveChangesAsync();
```

## Available Chain Values

Common chains from `Nethereum.Signer.Chain`:
- `Chain.MainNet` = 1 (Ethereum Mainnet)
- `Chain.Sepolia` = 11155111 (Ethereum Testnet)
- `Chain.Polygon` = 137
- `Chain.BinanceSmartChain` = 56
- `Chain.Avalanche` = 43114
- `Chain.Optimism` = 10
- `Chain.Arbitrum` = 42161

## IRpcUrlProvider Interface

```csharp
public interface IRpcUrlProvider
{
    Task<string> GetBestRpcUrlAsync(Chain chain, CancellationToken cancellationToken = default);
    Task<string> GetNextRpcUrlAsync(Chain chain, string excludeUrl, CancellationToken cancellationToken = default);
    Task MarkAsSuccessAsync(string url, CancellationToken cancellationToken = default);
    Task MarkAsFailedAsync(string url, Exception exception, CancellationToken cancellationToken = default);
}
```

## Exception Handling

```csharp
using RpcProvider.Core.Exceptions;

try
{
    var rpcUrl = await rpcProvider.GetBestRpcUrlAsync(Chain.MainNet);
    // Use the RPC URL...
}
catch (NoHealthyRpcException ex)
{
    // No healthy endpoints available for the requested chain
    _logger.LogError(ex, "No healthy RPC endpoints available");
}
```

## Background Health Checks

For automatic health monitoring, install the companion package:

```bash
dotnet add package RpcProvider.HealthWorker
```

See the [Health Check Worker](#health-check-worker) section below for details on background health monitoring.

## Requirements

- .NET 10.0 or higher
- Entity Framework Core
- Nethereum.Web3 5.0.0+
- Redis (optional, for distributed caching)

---

## Health Check Worker

This package includes a background health check service that automatically monitors and restores failed RPC endpoints.

### Features

- 🔍 **Automatic Health Checks**: Periodically tests failed endpoints using Nethereum.Web3
- 🔄 **Auto-Recovery**: Automatically restores recovered endpoints to active state
- ⏱️ **Exponential Backoff**: Smart retry timing to avoid overwhelming failed endpoints
- 🎯 **Configurable Intervals**: Adjust health check frequency to your needs
- 📊 **Error Reset**: Automatically resets consecutive error counts on recovery

### Quick Start

#### 1. Add Health Worker Service

```csharp
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

#### 2. Configure Options

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

### How It Works

The health worker runs as a background service (`IHostedService`) that:

1. **Runs Periodically**: Executes every N minutes (configurable via `HealthCheckIntervalMinutes`)
2. **Queries Failed Endpoints**: Gets all endpoints in `Error` state from the database
3. **Respects Exponential Backoff**: Only tests endpoints whose backoff period has elapsed
4. **Tests with Nethereum**: Calls `web3.Eth.Blocks.GetBlockNumber.SendRequestAsync()` to verify the endpoint
5. **Restores on Success**: Marks recovered endpoints as `Active` and resets error counts
6. **Logs Activity**: Provides detailed logging of health check operations

### Exponential Backoff Strategy

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

### Disabling the Worker

To temporarily disable health checks:

```json
{
  "RpcProvider": {
    "EnableHealthChecks": false
  }
}
```

Or remove the `AddRpcHealthCheckWorker()` registration.

### Best Practices

1. **Set Appropriate Intervals**: Balance between quick recovery and system load
   - High-traffic apps: 5-10 minutes
   - Low-traffic apps: 15-30 minutes

2. **Monitor Logs**: Watch for patterns in endpoint failures

3. **Adjust Backoff Settings**: Tune based on your RPC providers' recovery times

## Links

- [GitHub Repository](https://github.com/Tricksfor-Organization/RpcProvider)
- [Usage Guide](https://github.com/Tricksfor-Organization/RpcProvider/blob/main/USAGE_GUIDE.md)
- [Full Documentation](https://github.com/Tricksfor-Organization/RpcProvider/blob/main/README.md)

## License

This project is licensed under the MIT License.
