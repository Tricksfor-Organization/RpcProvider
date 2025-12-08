# RPC Provider Library

A robust .NET library for managing blockchain RPC endpoints with automatic failover, health monitoring, and intelligent endpoint selection.

## Features

- ✅ **Automatic Failover**: Seamlessly switches to alternative endpoints when failures occur
- ✅ **Health Monitoring**: Background service that tests failed endpoints and marks them as active when recovered
- ✅ **Intelligent Selection**: Chooses endpoints based on priority and error count
- ✅ **Redis Caching**: Fast endpoint retrieval with distributed caching support
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
            entity.HasIndex(e => new { e.ChainId, e.State, e.Priority });
            entity.Property(e => e.ChainId).IsRequired().HasMaxLength(50);
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
using Microsoft.EntityFrameworkCore;

public class RpcRepository : IRpcRepository
{
    private readonly ApplicationDbContext _context;

    public RpcRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<RpcEndpoint>> GetByChainAndStateAsync(
        string chainId, 
        RpcState state, 
        CancellationToken cancellationToken = default)
    {
        return await _context.RpcEndpoints
            .Where(e => e.ChainId == chainId && e.State == state)
            .OrderBy(e => e.Priority)
            .ThenBy(e => e.ConsecutiveErrors)
            .ToListAsync(cancellationToken);
    }

    public async Task<IEnumerable<RpcEndpoint>> GetByChainAsync(
        string chainId, 
        CancellationToken cancellationToken = default)
    {
        return await _context.RpcEndpoints
            .Where(e => e.ChainId == chainId)
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
        endpoint.UpdatedAt = DateTime.UtcNow;
        _context.RpcEndpoints.Update(endpoint);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task AddAsync(
        RpcEndpoint endpoint, 
        CancellationToken cancellationToken = default)
    {
        endpoint.CreatedAt = DateTime.UtcNow;
        endpoint.UpdatedAt = DateTime.UtcNow;
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

// Add Redis distributed cache (via Aspire or manually)
builder.AddRedisDistributedCache("redis");

// Register RPC Repository
builder.Services.AddScoped<IRpcRepository, RpcRepository>();

// Register RPC URL Provider (from shared library)
builder.Services.AddRpcUrlProvider(builder.Configuration);

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
    "MaxBackoffMinutes": 30
  }
}
```

### 5. Create Migration

```bash
dotnet ef migrations add AddRpcEndpointsTable
dotnet ef database update
```

### 6. Seed Initial Data

```csharp
// Add some RPC endpoints to your database
var endpoints = new List<RpcEndpoint>
{
    new RpcEndpoint
    {
        Id = Guid.NewGuid(),
        ChainId = "Ethereum",
        Url = "https://eth-mainnet.g.alchemy.com/v2/YOUR_API_KEY",
        State = RpcState.Active,
        Priority = 1,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    },
    new RpcEndpoint
    {
        Id = Guid.NewGuid(),
        ChainId = "Ethereum",
        Url = "https://mainnet.infura.io/v3/YOUR_API_KEY",
        State = RpcState.Active,
        Priority = 2,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    },
    new RpcEndpoint
    {
        Id = Guid.NewGuid(),
        ChainId = "BSC",
        Url = "https://bsc-dataseed1.binance.org/",
        State = RpcState.Active,
        Priority = 1,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow
    }
};

await context.RpcEndpoints.AddRangeAsync(endpoints);
await context.SaveChangesAsync();
```

## Usage Examples

### Basic Usage with Nethereum

```csharp
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

    public async Task<string> GetBalanceAsync(string address, string chainId)
    {
        string rpcUrl = await _rpcProvider.GetBestRpcUrlAsync(chainId);

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
}
```

### Advanced Usage with Automatic Retry

```csharp
public class BlockchainService
{
    private readonly IRpcUrlProvider _rpcProvider;
    private readonly ILogger<BlockchainService> _logger;
    private const int MaxRetries = 3;

    public async Task<string> GetBalanceWithRetryAsync(string address, string chainId)
    {
        string? lastFailedUrl = null;

        for (int attempt = 0; attempt < MaxRetries; attempt++)
        {
            try
            {
                // Get next available RPC (excluding previously failed one)
                string rpcUrl = lastFailedUrl == null
                    ? await _rpcProvider.GetBestRpcUrlAsync(chainId)
                    : await _rpcProvider.GetNextRpcUrlAsync(chainId, lastFailedUrl);

                var web3 = new Web3(rpcUrl);
                var balance = await web3.Eth.GetBalance.SendRequestAsync(address);

                // Mark as successful
                await _rpcProvider.MarkAsSuccessAsync(rpcUrl);

                return Web3.Convert.FromWei(balance.Value).ToString();
            }
            catch (NoHealthyRpcException)
            {
                _logger.LogCritical("No healthy RPC endpoints available for {ChainId}", chainId);
                throw; // Can't retry, no endpoints available
            }
            catch (Exception ex) when (IsRpcException(ex))
            {
                var currentRpc = await _rpcProvider.GetBestRpcUrlAsync(chainId);
                lastFailedUrl = currentRpc;
                await _rpcProvider.MarkAsFailedAsync(currentRpc, ex);

                _logger.LogWarning(ex, 
                    "RPC call failed on attempt {Attempt}/{MaxRetries}, retrying with different endpoint",
                    attempt + 1, MaxRetries);

                if (attempt == MaxRetries - 1)
                    throw new RpcProviderException(
                        $"All retry attempts failed for chain {chainId}", ex);
            }
        }

        throw new RpcProviderException($"Failed after {MaxRetries} attempts");
    }

    private bool IsRpcException(Exception ex) =>
        ex is HttpRequestException or TimeoutException or RpcException;
}
```

## Configuration Options

| Option | Type | Default | Description |
|--------|------|---------|-------------|
| `CacheDurationSeconds` | int | 300 | Duration in seconds to cache healthy RPC endpoints |
| `MaxConsecutiveErrorsBeforeDisable` | int | 5 | Maximum consecutive errors before marking endpoint as Error |
| `RequestTimeoutSeconds` | int | 30 | Request timeout in seconds for RPC calls |
| `AllowDisabledEndpointsAsFallback` | bool | false | Whether to use disabled endpoints as last resort |
| `HealthCheckIntervalMinutes` | int | 5 | Interval in minutes for health check background service |
| `EnableHealthChecks` | bool | true | Enable or disable health checks |
| `BaseBackoffMinutes` | int | 1 | Base backoff time for exponential backoff |
| `MaxBackoffMinutes` | int | 30 | Maximum backoff time for exponential backoff |

## Database Schema

### RpcEndpoint Table

| Column | Type | Description |
|--------|------|-------------|
| Id | Guid | Primary key |
| ChainId | string(50) | Chain identifier (e.g., "Ethereum", "BSC") |
| Url | string(500) | RPC endpoint URL |
| State | int | State: 0=Active, 1=Error, 2=Disabled |
| Priority | int | Selection priority (lower = higher priority) |
| ConsecutiveErrors | int | Number of consecutive errors |
| ErrorMessage | string? | Last error message |
| LastErrorAt | DateTime? | Timestamp of last error |
| CreatedAt | DateTime | Creation timestamp |
| UpdatedAt | DateTime | Last update timestamp |

**Indexes:**
- Primary Key: `Id`
- Composite Index: `(ChainId, State, Priority)`

## How It Works

### Endpoint Selection Strategy

1. **Retrieve from Cache**: Check Redis for cached endpoint
2. **Query Active Endpoints**: Get all Active state endpoints for the chain
3. **Fallback to Error State**: If no Active endpoints, try Error state endpoints (respecting exponential backoff)
4. **Emergency Mode**: Optionally use Disabled endpoints as last resort
5. **Sort by Priority**: Order by Priority ASC, then ConsecutiveErrors ASC
6. **Cache Result**: Store selected endpoint in Redis

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
3. Makes test RPC call (`eth_blockNumber`)
4. Marks recovered endpoints as Active
5. Resets consecutive error count

## Exception Handling

### NoHealthyRpcException

Thrown when no healthy endpoints are available for a chain. This indicates:
- All Active endpoints have failed
- Error state endpoints are still in backoff period
- Disabled endpoints are not allowed as fallback

**Handling:**
```csharp
try
{
    var rpcUrl = await _rpcProvider.GetBestRpcUrlAsync("Ethereum");
}
catch (NoHealthyRpcException ex)
{
    _logger.LogCritical(ex, "No healthy RPC endpoints for chain {ChainId}", ex.ChainId);
    // Notify ops team, trigger alert, etc.
}
```

### RpcProviderException

Generic exception for RPC provider operations.

## Best Practices

1. **Always Mark Results**: Call `MarkAsSuccessAsync()` or `MarkAsFailedAsync()` after RPC calls
2. **Use Retry Logic**: Implement automatic retry with `GetNextRpcUrlAsync()`
3. **Monitor Health**: Enable the health check worker in production
4. **Configure Caching**: Adjust cache duration based on your traffic
5. **Set Priorities**: Assign lower priority values to faster/more reliable endpoints
6. **Handle Exceptions**: Catch and handle `NoHealthyRpcException` appropriately
7. **Use Scoped Services**: Register repository as Scoped for proper DbContext lifecycle

## Project Structure

```
RpcProvider/
├── src/
│   ├── RpcProvider.Core/              # Main shared library
│   │   ├── Models/                     # Entity models
│   │   ├── Interfaces/                 # Service interfaces
│   │   ├── Services/                   # Service implementations
│   │   ├── Exceptions/                 # Custom exceptions
│   │   ├── Configuration/              # Configuration options
│   │   └── Extensions/                 # DI extensions
│   └── RpcProvider.HealthWorker/      # Background health check
│       ├── RpcHealthCheckWorker.cs
│       └── HealthWorkerExtensions.cs
├── tests/                              # Unit tests (to be added)
├── samples/                            # Sample projects (to be added)
└── README.md
```

## Contributing

Contributions are welcome! Please submit issues and pull requests to the GitHub repository.

## License

MIT License - see LICENSE file for details.