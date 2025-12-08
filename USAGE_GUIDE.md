# RPC Provider - Quick Integration Guide

## Step-by-Step Integration

### Step 1: Add to Your Project

Add project references:
```bash
cd YourProject.Infrastructure
dotnet add reference path/to/RpcProvider.Core/RpcProvider.Core.csproj
dotnet add reference path/to/RpcProvider.HealthWorker/RpcProvider.HealthWorker.csproj
```

### Step 2: Update Your DbContext

```csharp
using RpcProvider.Core.Models;

public class YourDbContext : DbContext
{
    // Your existing DbSets
    public DbSet<User> Users { get; set; }
    
    // Add this
    public DbSet<RpcEndpoint> RpcEndpoints { get; set; }
    
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        
        // Add this configuration
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

### Step 3: Create Repository Implementation

Create `Infrastructure/Repositories/RpcRepository.cs`:

```csharp
using RpcProvider.Core.Interfaces;
using RpcProvider.Core.Models;
using Microsoft.EntityFrameworkCore;

public class RpcRepository : IRpcRepository
{
    private readonly YourDbContext _context;

    public RpcRepository(YourDbContext context)
    {
        _context = context;
    }

    public async Task<IEnumerable<RpcEndpoint>> GetByChainAndStateAsync(
        string chainId, RpcState state, CancellationToken ct = default)
    {
        return await _context.RpcEndpoints
            .Where(e => e.ChainId == chainId && e.State == state)
            .OrderBy(e => e.Priority)
            .ThenBy(e => e.ConsecutiveErrors)
            .ToListAsync(ct);
    }

    public async Task<IEnumerable<RpcEndpoint>> GetByChainAsync(
        string chainId, CancellationToken ct = default)
    {
        return await _context.RpcEndpoints
            .Where(e => e.ChainId == chainId)
            .ToListAsync(ct);
    }

    public async Task<RpcEndpoint?> GetByUrlAsync(string url, CancellationToken ct = default)
    {
        return await _context.RpcEndpoints.FirstOrDefaultAsync(e => e.Url == url, ct);
    }

    public async Task<RpcEndpoint?> GetByIdAsync(Guid id, CancellationToken ct = default)
    {
        return await _context.RpcEndpoints.FindAsync(new object[] { id }, ct);
    }

    public async Task UpdateAsync(RpcEndpoint endpoint, CancellationToken ct = default)
    {
        endpoint.UpdatedAt = DateTime.UtcNow;
        _context.RpcEndpoints.Update(endpoint);
        await _context.SaveChangesAsync(ct);
    }

    public async Task AddAsync(RpcEndpoint endpoint, CancellationToken ct = default)
    {
        endpoint.CreatedAt = DateTime.UtcNow;
        endpoint.UpdatedAt = DateTime.UtcNow;
        await _context.RpcEndpoints.AddAsync(endpoint, ct);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<IEnumerable<RpcEndpoint>> GetAllAsync(CancellationToken ct = default)
    {
        return await _context.RpcEndpoints.ToListAsync(ct);
    }
}
```

### Step 4: Register in DI Container

Update your `Program.cs` or `Startup.cs`:

```csharp
using RpcProvider.Core.Extensions;
using RpcProvider.Core.Interfaces;
using RpcProvider.HealthWorker;

// ... your existing code

// Register your repository
builder.Services.AddScoped<IRpcRepository, RpcRepository>();

// Register RPC Provider services
builder.Services.AddRpcUrlProvider(builder.Configuration);

// Optional: Add health check worker
builder.Services.AddRpcHealthCheckWorker();
```

### Step 5: Add Configuration

Update `appsettings.json`:

```json
{
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

### Step 6: Create Migration

```bash
dotnet ef migrations add AddRpcEndpointsTable
dotnet ef database update
```

### Step 7: Seed Data (Optional)

Create a database seeder or use EF migrations to seed initial RPC endpoints:

```csharp
public static class RpcEndpointSeeder
{
    public static void SeedRpcEndpoints(YourDbContext context)
    {
        if (context.RpcEndpoints.Any()) return;

        var endpoints = new[]
        {
            new RpcEndpoint
            {
                Id = Guid.NewGuid(),
                ChainId = "Ethereum",
                Url = "https://eth-mainnet.g.alchemy.com/v2/YOUR_KEY",
                State = RpcState.Active,
                Priority = 1,
                ConsecutiveErrors = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new RpcEndpoint
            {
                Id = Guid.NewGuid(),
                ChainId = "Ethereum",
                Url = "https://mainnet.infura.io/v3/YOUR_KEY",
                State = RpcState.Active,
                Priority = 2,
                ConsecutiveErrors = 0,
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
                ConsecutiveErrors = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            },
            new RpcEndpoint
            {
                Id = Guid.NewGuid(),
                ChainId = "Polygon",
                Url = "https://polygon-rpc.com",
                State = RpcState.Active,
                Priority = 1,
                ConsecutiveErrors = 0,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        };

        context.RpcEndpoints.AddRange(endpoints);
        context.SaveChanges();
    }
}
```

### Step 8: Use in Your Services

```csharp
using Nethereum.Web3;
using RpcProvider.Core.Interfaces;
using RpcProvider.Core.Exceptions;

public class YourBlockchainService
{
    private readonly IRpcUrlProvider _rpcProvider;
    private readonly ILogger<YourBlockchainService> _logger;

    public YourBlockchainService(
        IRpcUrlProvider rpcProvider,
        ILogger<YourBlockchainService> logger)
    {
        _rpcProvider = rpcProvider;
        _logger = logger;
    }

    public async Task<string> GetBalanceAsync(string address, string chainId)
    {
        string? lastFailedUrl = null;
        const int maxRetries = 3;

        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            try
            {
                // Get RPC URL
                string rpcUrl = lastFailedUrl == null
                    ? await _rpcProvider.GetBestRpcUrlAsync(chainId)
                    : await _rpcProvider.GetNextRpcUrlAsync(chainId, lastFailedUrl);

                _logger.LogDebug("Using RPC: {RpcUrl}", rpcUrl);

                // Make blockchain call
                var web3 = new Web3(rpcUrl);
                var balance = await web3.Eth.GetBalance.SendRequestAsync(address);

                // Mark as successful
                await _rpcProvider.MarkAsSuccessAsync(rpcUrl);

                return Web3.Convert.FromWei(balance.Value).ToString();
            }
            catch (NoHealthyRpcException)
            {
                _logger.LogCritical("No healthy RPC endpoints for {ChainId}", chainId);
                throw;
            }
            catch (Exception ex) when (IsRpcException(ex))
            {
                var currentRpc = lastFailedUrl ?? await _rpcProvider.GetBestRpcUrlAsync(chainId);
                lastFailedUrl = currentRpc;
                
                await _rpcProvider.MarkAsFailedAsync(currentRpc, ex);
                
                _logger.LogWarning(ex, 
                    "RPC call failed (attempt {Attempt}/{MaxRetries})", 
                    attempt + 1, maxRetries);

                if (attempt == maxRetries - 1)
                    throw new RpcProviderException(
                        $"All retry attempts failed for {chainId}", ex);
            }
        }

        throw new RpcProviderException($"Failed after {maxRetries} attempts");
    }

    private bool IsRpcException(Exception ex) =>
        ex is HttpRequestException or 
              TimeoutException or 
              Nethereum.JsonRpc.Client.RpcClientTimeoutException or
              Nethereum.JsonRpc.Client.RpcException;
}
```

## Common Chain IDs

Use consistent chain IDs across your application:

```csharp
public static class ChainIds
{
    public const string Ethereum = "Ethereum";
    public const string BSC = "BSC";
    public const string Polygon = "Polygon";
    public const string Arbitrum = "Arbitrum";
    public const string Optimism = "Optimism";
    public const string Avalanche = "Avalanche";
    public const string Fantom = "Fantom";
}
```

## Troubleshooting

### Issue: "No healthy RPC endpoints available"

**Solutions:**
1. Check database has endpoints for that chain
2. Verify endpoints aren't all in Error state
3. Check Redis connection is working
4. Enable `AllowDisabledEndpointsAsFallback` for emergency mode

### Issue: Endpoints keep getting marked as Error

**Solutions:**
1. Verify API keys are valid
2. Check network connectivity
3. Increase `RequestTimeoutSeconds`
4. Verify RPC provider isn't rate limiting
5. Check health check worker logs

### Issue: Cache not working

**Solutions:**
1. Verify Redis is running
2. Check Redis connection string
3. Ensure distributed cache is registered in DI
4. Check cache duration settings

## Monitoring Recommendations

1. **Log all NoHealthyRpcException occurrences** - these are critical
2. **Monitor consecutive error counts** - alert if multiple endpoints failing
3. **Track endpoint state changes** - know when endpoints go down/recover
4. **Monitor cache hit rate** - ensure caching is effective
5. **Alert on health check failures** - know when recovery attempts fail

## Performance Tips

1. **Adjust cache duration** based on your traffic patterns
2. **Set appropriate priorities** on endpoints (faster ones = lower priority number)
3. **Use health check worker** to automatically recover failed endpoints
4. **Implement circuit breakers** in your application layer if needed
5. **Consider geographic distribution** of endpoints for global apps

## Security Considerations

1. **Store API keys in secrets** (Azure Key Vault, AWS Secrets Manager, etc.)
2. **Use separate keys per environment** (dev, staging, prod)
3. **Rotate keys regularly** and update in database
4. **Monitor for suspicious patterns** (excessive failures might indicate attack)
5. **Implement rate limiting** at your application level

## Next Steps

1. Integrate into your existing services
2. Add endpoints for all chains you support
3. Configure monitoring and alerts
4. Test failover scenarios
5. Document your specific chain IDs and priorities
