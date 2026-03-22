# RPC Provider Library

A robust .NET library for managing blockchain RPC endpoints with automatic failover, health monitoring, and intelligent endpoint selection.

## Features

- ✅ **Automatic Failover**: Seamlessly switches to alternative endpoints when failures occur
- ✅ **ExecuteWithRetry**: Built-in retry mechanism that automatically tries all available endpoints
- ✅ **Health Monitoring**: Background service that tests failed endpoints and marks them as active when recovered
- ✅ **Intelligent Selection**: Chooses endpoints based on priority and error count
- ✅ **HybridCache**: In-memory + distributed (Redis) caching with automatic fallback
- ✅ **Exponential Backoff**: Prevents overwhelming failed endpoints with requests
- ✅ **Multi-Chain Support**: Manages endpoints for multiple blockchain networks
- ✅ **Clean Architecture**: Designed to fit in Infrastructure layer
- ✅ **Per-Project Database**: Each project maintains its own RPC endpoint table

## Architecture

```
┌─────────────────────────────────────────────────────────────┐
│                    Your Application                          │
│  ┌──────────────┐  ┌──────────────┐  ┌──────────────┐      │
│  │  ProjectA    │  │  ProjectB    │  │  ProjectC    │      │
│  │  Web API     │  │  Web API     │  │  Worker      │      │
│  └──────┬───────┘  └──────┬───────┘  └──────┬───────┘      │
│         │                  │                  │              │
│         └──────────────────┴──────────────────┘              │
│                         │                                    │
│         ┌───────────────▼────────────────┐                  │
│         │  RpcProvider.Core (Shared)     │                  │
│         │  - IRpcUrlProvider              │                  │
│         │  - RpcUrlProvider               │                  │
│         │  - RpcHealthChecker             │                  │
│         └────────────────────────────────┘                  │
│                         │                                    │
│         ┌───────────────▼────────────────┐                  │
│         │  RpcProvider.HealthWorker      │                  │
│         │  - Background Health Checks    │                  │
│         └────────────────────────────────┘                  │
└─────────────────────────────────────────────────────────────┘
         │                  │                  │
         ▼                  ▼                  ▼
┌─────────────┐    ┌─────────────┐    ┌─────────────┐
│ ProjectA DB │    │ ProjectB DB │    │ ProjectC DB │
│ RpcEndpoints│    │ RpcEndpoints│    │ RpcEndpoints│
└─────────────┘    └─────────────┘    └─────────────┘
```

## Installation

Add the project references to your application:

```bash
dotnet add reference path/to/RpcProvider.Core/RpcProvider.Core.csproj
dotnet add reference path/to/RpcProvider.HealthWorker/RpcProvider.HealthWorker.csproj
```

## Quick Start

### 1. Add RpcEndpoint Entity to Your DbContext

```csharp
using RpcProvider.Core.Models;
using Nethereum.Signer;
using Microsoft.EntityFrameworkCore;

public class ApplicationDbContext : DbContext
{
    public DbSet<User> Users { get; set; }
    // ... your existing entities

    // Add RpcEndpoint table
    public DbSet<RpcEndpoint> RpcEndpoints { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure RpcEndpoint
        modelBuilder.Entity<RpcEndpoint>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.Chain, e.State, e.Priority });
            entity.Property(e => e.Chain).IsRequired().HasConversion<long>();
            entity.Property(e => e.Url).IsRequired().HasMaxLength(500);
            entity.Property(e => e.State).HasConversion<int>();
        });
    }
}
```

### 2. Implement IRpcRepository

```csharp
using RpcProvider.Core.Interfaces;
using RpcProvider.Core.Models;
using Nethereum.Signer;
using Microsoft.EntityFrameworkCore;

public class RpcRepository : IRpcRepository
{
    private readonly ApplicationDbContext _context;

    public RpcRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<RpcEndpoint>> GetByChainAndStateAsync(
        Chain chain, 
        RpcState state, 
        CancellationToken cancellationToken = default)
    {
        return await _context.RpcEndpoints
            .Where(e => e.Chain == chain && e.State == state)
            .OrderBy(e => e.Priority)
            .ThenBy(e => e.ConsecutiveErrors)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<RpcEndpoint>> GetByChainAsync(
        Chain chain, 
        CancellationToken cancellationToken = default)
    {
        return await _context.RpcEndpoints
            .Where(e => e.Chain == chain)
            .ToListAsync(cancellationToken);
    }

    public async Task<RpcEndpoint?> GetByUrlAsync(
        string url, 
        CancellationToken cancellationToken = default)
    {
        return await _context.RpcEndpoints
            .FirstOrDefaultAsync(e => e.Url == url, cancellationToken);
    }

    public async Task<RpcEndpoint?> GetByIdAsync(
        Guid id, 
        CancellationToken cancellationToken = default)
    {
        return await _context.RpcEndpoints.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task UpdateAsync(
        RpcEndpoint endpoint, 
        CancellationToken cancellationToken = default)
    {
        endpoint.Modified = DateTime.UtcNow;
        _context.RpcEndpoints.Update(endpoint);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(
        RpcEndpoint endpoint, 
        CancellationToken cancellationToken = default)
    {
        endpoint.Created = DateTime.UtcNow;
        endpoint.Modified = DateTime.UtcNow;
        await _context.RpcEndpoints.AddAsync(endpoint, cancellationToken);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IEnumerable<RpcEndpoint>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        return await _context.RpcEndpoints.ToListAsync(cancellationToken);
    }
}
```

### 3. Register Services in Program.cs

```csharp
using RpcProvider.Core.Extensions;
using RpcProvider.HealthWorker;

var builder = WebApplication.CreateBuilder(args);

// Add DbContext
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Optional: Add Redis for distributed caching (via Aspire or manually)
// If Redis is not configured, HybridCache automatically uses in-memory caching only
builder.AddRedisDistributedCache("redis"); // Aspire
// OR manually:
// builder.Services.AddStackExchangeRedisCache(options => { options.Configuration = "localhost:6379"; });

// Register RPC Repository
builder.Services.AddScoped<IRpcRepository, RpcRepository>();

// Register RPC URL Provider (from shared library)
builder.Services.AddRpcUrlProvider(builder.Configuration);

// Or with action-based configuration:
// builder.Services.AddRpcUrlProvider((options, services) =>
// {
//     options.CacheDurationSeconds = 300;
//     options.MaxConsecutiveErrorsBeforeDisable = 5;
// });

// Register Health Check Worker (optional)
builder.Services.AddRpcHealthCheckWorker();

var app = builder.Build();
app.Run();
```

### 4. Configure appsettings.json

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=MyApp;Trusted_Connection=true;"
  },
  "RpcProvider": {
    "CacheDurationSeconds": 300,
    "MaxConsecutiveErrorsBeforeDisable": 5,
    "RequestTimeoutSeconds": 30,
    "AllowDisabledEndpointsAsFallback": false,
    "HealthCheckIntervalMinutes": 5,
    "EnableHealthChecks": true,
    "BaseBackoffMinutes": 1,
    "MaxBackoffMinutes": 30,
    "CacheKeyPrefix": "MyApp"
  }
}
```

**Note:** The `CacheKeyPrefix` is optional but recommended when multiple projects share the same Redis cache. This prevents cache key conflicts between projects.

### 5. Create Migration

```bash
dotnet ef migrations add AddRpcEndpointsTable
dotnet ef database update
```

### 6. Seed Initial Data

```csharp
using Nethereum.Signer;
using RpcProvider.Core.Models;

// Add some RPC endpoints to your database
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
        Chain = Chain.MainNet,
        Url = "https://mainnet.infura.io/v3/YOUR_API_KEY",
        State = RpcState.Active,
        Priority = 2,
        Created = DateTime.UtcNow,
        Modified = DateTime.UtcNow
    },
    new RpcEndpoint
    {
        Id = Guid.NewGuid(),
        Chain = Chain.Binance, // BSC Mainnet
        Url = "https://bsc-dataseed1.binance.org/",
        State = RpcState.Active,
        Priority = 1,
        Created = DateTime.UtcNow,
        Modified = DateTime.UtcNow
    }
};

await context.RpcEndpoints.AddRangeAsync(endpoints);
await context.SaveChangesAsync();
```

## Usage Examples

### Basic Usage with Nethereum

```csharp
using Nethereum.Signer;
using Nethereum.Web3;
using RpcProvider.Core.Interfaces;

public class BlockchainService
{
    private readonly IRpcUrlProvider _rpcProvider;
    private readonly ILogger<BlockchainService> _logger;

    public BlockchainService(
        IRpcUrlProvider rpcProvider,
        ILogger<BlockchainService> logger)
    {
        _rpcProvider = rpcProvider;
        _logger = logger;
    }

    public async Task<string> GetBalanceAsync(string address, Chain chain)
    {
        string rpcUrl = await _rpcProvider.GetBestRpcUrlAsync(chain);

        try
        {
            var web3 = new Web3(rpcUrl);
            var balance = await web3.Eth.GetBalance.SendRequestAsync(address);

            // Mark as successful
            await _rpcProvider.MarkAsSuccessAsync(rpcUrl);

            return Web3.Convert.FromWei(balance.Value).ToString();
        }
        catch (Exception ex) when (IsRpcException(ex))
        {
            _logger.LogWarning(ex, "RPC call failed, marking endpoint as failed");
            await _rpcProvider.MarkAsFailedAsync(rpcUrl, ex);
            throw;
        }
    }

    private bool IsRpcException(Exception ex) =>
        ex is HttpRequestException or TimeoutException or RpcException;
```csharp
    }

    // For critical operations, ignore backoff to try all endpoints immediately
    public async Task<string> GetCriticalDataAsync(Chain chain, string contractAddress)
    {
        return await _rpcProvider.ExecuteWithRetryAsync(
            chain,
            async (rpcUrl, ct) =>
            {
                var web3 = new Web3(rpcUrl);
                // Replace with your actual critical blockchain operation
                var contract = web3.Eth.GetContract(/* ABI */, contractAddress);
                var data = await contract.GetFunction("yourMethod").CallAsync<string>();
                return data;
            },
            ignoreBackoff: true); // Force immediate retry of all endpoints
    }
}
```

### Advanced Usage with ExecuteWithRetry (Recommended)

The `ExecuteWithRetryAsync` method automatically handles retries across all available RPC endpoints:

```csharp
using Nethereum.Signer;
using Nethereum.Web3;
using RpcProvider.Exceptions;

public class BlockchainService
{
    private readonly IRpcUrlProvider _rpcProvider;
    private readonly ILogger<BlockchainService> _logger;

    public BlockchainService(
        IRpcUrlProvider rpcProvider,
        ILogger<BlockchainService> logger)
    {
        _rpcProvider = rpcProvider;
        _logger = logger;
    }

    public async Task<decimal> GetBalanceAsync(string address, Chain chain)
    {
        try
        {
            // Automatically tries all available endpoints until success
            var balance = await _rpcProvider.ExecuteWithRetryAsync(
                chain,
                async (rpcUrl, ct) =>
                {
                    var web3 = new Web3(rpcUrl);
                    var balanceWei = await web3.Eth.GetBalance.SendRequestAsync(address);
                    return Web3.Convert.FromWei(balanceWei.Value);
                });

            return balance;
        }
        catch (AllRpcEndpointsFailedException ex)
        {
            _logger.LogError(ex, 
                "All {Count} RPC endpoints failed for chain {Chain}",
                ex.Failures.Count, chain);
            
            // Log individual failures
            foreach (var failure in ex.Failures)
            {
                _logger.LogDebug("  - {Url}: {Error}", 
                    failure.Url, failure.Exception.Message);
            }
            
            throw;
        }
    }

    // For critical operations, ignore backoff to try all endpoints immediately
    public async Task<string> GetCriticalDataAsync(Chain chain, string contractAddress)
    {
        return await _rpcProvider.ExecuteWithRetryAsync(
            chain,
            async (rpcUrl, ct) =>
            {
                var web3 = new Web3(rpcUrl);
                // Your critical blockchain operation
                return await SomeImportantOperation(web3, contractAddress, ct);
            },
            ignoreBackoff: true); // Force immediate retry of all endpoints
    }
}
```

**Benefits of ExecuteWithRetry:**
- ✅ No manual retry loops
- ✅ Automatic endpoint tracking (success/failure)
- ✅ Respects exponential backoff by default
- ✅ Can override backoff for critical operations
- ✅ Comprehensive error tracking with `AllRpcEndpointsFailedException`

**See [EXECUTE_WITH_RETRY_GUIDE.md](EXECUTE_WITH_RETRY_GUIDE.md) for complete examples.**

### Manual Retry Pattern (Alternative)

If you need more control, you can use the manual retry pattern:

```csharp
public async Task<string> GetBalanceWithManualRetryAsync(string address, Chain chain)
{
    string? lastFailedUrl = null;
    const int maxRetries = 3;

    for (int attempt = 0; attempt < maxRetries; attempt++)
    {
        try
        {
            string rpcUrl = lastFailedUrl == null
                ? await _rpcProvider.GetBestRpcUrlAsync(chain)
                : await _rpcProvider.GetNextRpcUrlAsync(chain, lastFailedUrl);

            var web3 = new Web3(rpcUrl);
            var balance = await web3.Eth.GetBalance.SendRequestAsync(address);

            await _rpcProvider.MarkAsSuccessAsync(rpcUrl);
            return Web3.Convert.FromWei(balance.Value).ToString();
        }
        catch (NoHealthyRpcException)
        {
            _logger.LogCritical("No healthy RPC endpoints available");
            throw;
        }
        catch (Exception ex) when (IsRpcException(ex))
        {
            lastFailedUrl = await _rpcProvider.GetBestRpcUrlAsync(chain);
            await _rpcProvider.MarkAsFailedAsync(lastFailedUrl, ex);
            
            if (attempt == maxRetries - 1) throw;
        }
    }

    throw new InvalidOperationException("Failed after all retries");
}

private bool IsRpcException(Exception ex) =>
    ex is HttpRequestException or TimeoutException or RpcException;
```

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `CacheDurationSeconds` | int | 300 | Duration in seconds to cache healthy RPC endpoints |
| `MaxConsecutiveErrorsBeforeDisable` | int | 5 | Maximum consecutive errors before marking endpoint as Error |
| `MaxRetryAttempts` | int | -1 | Maximum retry attempts in ExecuteWithRetryAsync (-1 = try all) |
| `RequestTimeoutSeconds` | int | 30 | Request timeout in seconds for RPC calls |
| `AllowDisabledEndpointsAsFallback` | bool | false | Whether to use disabled endpoints as last resort |
| `HealthCheckIntervalMinutes` | int | 5 | Interval in minutes for health check background service |
| `EnableHealthChecks` | bool | true | Enable or disable health checks |
| `BaseBackoffMinutes` | int | 1 | Base backoff time for exponential backoff |
| `MaxBackoffMinutes` | int | 30 | Maximum backoff time for exponential backoff |
| `CacheKeyPrefix` | string? | null | Cache key prefix to isolate cache entries between projects sharing the same cache backend (e.g., Redis). Example: "ProjectA", "MyApp" |

## Database Schema

### RpcEndpoint Table

| Column | Type | Description |
|--------|------|-------------|
| Id | Guid | Primary key |
| Chain | long | Chain enum value from Nethereum (e.g., 1=MainNet, 137=Polygon) |
| Url | string(500) | RPC endpoint URL |
| State | int | State: 0=Active, 1=Error, 2=Disabled |
| Priority | int | Selection priority (lower = higher priority) |
| ConsecutiveErrors | int | Number of consecutive errors |
| ErrorMessage | string? | Last error message |
| LastErrorAt | DateTime? | Timestamp of last error |
| Created | DateTime | Creation timestamp |
| Modified | DateTime | Last update timestamp |

**Indexes:**
- Primary Key: `Id`
- Composite Index: `(Chain, State, Priority)`

## How It Works

### Endpoint Selection Strategy

1. **Retrieve from Cache**: Check HybridCache (L1: in-memory, L2: Redis if configured)
2. **Query Active Endpoints**: Get all Active state endpoints for the chain
3. **Fallback to Error State**: If no Active endpoints, try Error state endpoints (respecting exponential backoff)
4. **Emergency Mode**: Optionally use Disabled endpoints as last resort
5. **Sort by Priority**: Order by Priority ASC, then ConsecutiveErrors ASC
6. **Cache Result**: Store selected endpoint in HybridCache (both L1 and L2)

### Caching Architecture

**HybridCache** provides two-level caching:
- **L1 (In-Memory)**: Fast local cache, no network round trip
- **L2 (Redis)**: Distributed cache shared across instances (if Redis is configured)

**Automatic Fallback:**
- If Redis is not configured → uses in-memory only
- If Redis connection fails → falls back to in-memory automatically
- When Redis recovers → automatically uses distributed caching again

This means your application works perfectly fine with or without Redis!

### Multi-Project Cache Isolation

When multiple projects share the same Redis cache instance, use `CacheKeyPrefix` to prevent cache conflicts:

**Example Scenario:**
- ProjectA (Web API) and ProjectB (Background Worker) both use RpcProvider
- Both connect to the same Redis instance
- Both query Ethereum Mainnet (Chain.MainNet = 1)

**Without CacheKeyPrefix:**
```
ProjectA cache key: "rpc:best:1"
ProjectB cache key: "rpc:best:1"  ❌ Conflict! Same key
```

**With CacheKeyPrefix:**
```csharp
// ProjectA appsettings.json
{
  "RpcProvider": {
    "CacheKeyPrefix": "ProjectA"
  }
}

// ProjectB appsettings.json
{
  "RpcProvider": {
    "CacheKeyPrefix": "ProjectB"
  }
}
```

Generated cache keys:
```
ProjectA cache key: "rpc:best:1:ProjectA"
ProjectB cache key: "rpc:best:1:ProjectB"  ✅ Isolated!
```

This ensures each project maintains its own cache entries and endpoint selections independently.

### Exponential Backoff

Failed endpoints use exponential backoff to prevent overwhelming them:

```
Error Count | Backoff Time
------------|-------------
1           | 1 minute
2           | 2 minutes
3           | 4 minutes
4           | 8 minutes
5           | 16 minutes
6+          | 30 minutes (max)
```

### Health Check Worker

The background worker:
1. Runs every N minutes (configurable)
2. Queries all Error state endpoints
3. Makes test RPC call using Nethereum (`web3.Eth.Blocks.GetBlockNumber.SendRequestAsync()`)
4. Marks recovered endpoints as Active
5. Resets consecutive error count

## Exception Handling

### AllRpcEndpointsFailedException (NEW)

Thrown by `ExecuteWithRetryAsync` when all available RPC endpoints have been tried and failed.

**Properties:**
- `Chain`: The blockchain chain that failed
- `Failures`: List of all attempted endpoints with their exceptions

**Handling:**
```csharp
using RpcProvider.Exceptions;

try
{
    var result = await _rpcProvider.ExecuteWithRetryAsync(
        Chain.MainNet,
        async (rpcUrl, ct) => await SomeOperation(rpcUrl, ct));
}
catch (AllRpcEndpointsFailedException ex)
{
    _logger.LogError("All {Count} endpoints failed for {Chain}", 
        ex.Failures.Count, ex.Chain);
    
    // Inspect individual failures
    foreach (var failure in ex.Failures)
    {
        _logger.LogDebug("  {Url}: {Error}", 
            failure.Url, failure.Exception.Message);
    }
    
    // Notify ops team, trigger alert, etc.
}
```

### NoHealthyRpcException

Thrown when no healthy endpoints are available for a chain. This indicates:
- All Active endpoints have failed
- Error state endpoints are still in backoff period
- Disabled endpoints are not allowed as fallback

**Handling:**
```csharp
try
{
    var rpcUrl = await _rpcProvider.GetBestRpcUrlAsync(Chain.MainNet);
}
catch (NoHealthyRpcException ex)
{
    _logger.LogCritical(ex, "No healthy RPC endpoints for chain {Chain}", ex.Chain);
    // Notify ops team, trigger alert, etc.
}
```

### RpcProviderException

Base exception for all RPC provider operations.

## Best Practices

1. **Use ExecuteWithRetry**: Prefer `ExecuteWithRetryAsync()` for automatic failover and retry logic
2. **Handle AllRpcEndpointsFailedException**: Catch this exception to handle total RPC failures
3. **Manual Tracking**: If not using ExecuteWithRetry, always call `MarkAsSuccessAsync()` or `MarkAsFailedAsync()`
4. **Critical Operations**: Use `ignoreBackoff: true` for time-sensitive operations
5. **Monitor Health**: Enable the health check worker in production
6. **Configure Caching**: Adjust cache duration based on your traffic
7. **Set Priorities**: Assign lower priority values to faster/more reliable endpoints
8. **Handle NoHealthyRpc**: Catch and handle `NoHealthyRpcException` for manual RPC selection
9. **Use Scoped Services**: Register repository as Scoped for proper DbContext lifecycle
10. **Cache Isolation**: Use `CacheKeyPrefix` when multiple projects share Redis

## Project Structure

```
RpcProvider/
├── src/
│   ├── RpcProvider/                   # Main library
│   │   ├── Models/                     # Entity models
│   │   ├── Interfaces/                 # Service interfaces
│   │   ├── Services/                   # Service implementations
│   │   ├── Exceptions/                 # Custom exceptions
│   │   ├── Configuration/              # Configuration options
│   │   └── Extensions/                 # DI extensions
├── tests/
│   └── RpcProvider.Tests/             # Comprehensive tests (69 tests)
├── samples/                            # Sample projects (to be added)
├── README.md
├── USAGE_GUIDE.md
└── EXECUTE_WITH_RETRY_GUIDE.md        # ExecuteWithRetry examples
```

## Contributing

Contributions are welcome! Please submit issues and pull requests to the GitHub repository.

## License

MIT License - see LICENSE file for details.