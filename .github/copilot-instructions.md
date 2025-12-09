# GitHub Copilot Instructions for RpcProvider

## Project Overview

RpcProvider is a .NET 10.0 library for managing blockchain RPC endpoints with automatic failover, health monitoring, and hybrid caching (in-memory + distributed). It provides intelligent endpoint selection, exponential backoff for failed endpoints, and background health checks using Nethereum.Web3.

**Key Technologies:**
- .NET 10.0 (C# 13 with primary constructors)
- Nethereum.Web3 5.0.0 for blockchain interactions
- Entity Framework Core for data persistence
- HybridCache (in-memory + Redis with automatic fallback)
- Background worker pattern

## Architecture Principles

### 1. Chain Identification
**CRITICAL:** Always use `Chain` enum from `Nethereum.Signer`, never strings.

```csharp
// ✅ CORRECT
using Nethereum.Signer;
Chain chain = Chain.MainNet;      // Ethereum Mainnet (value: 1)
Chain polygon = Chain.Polygon;     // Polygon (value: 137)
Chain bsc = Chain.BinanceSmartChain; // BSC (value: 56)

// ❌ INCORRECT - Never use strings
string chainId = "Ethereum";       // Type mismatch
string chainId = "1";              // Wrong type
```

**Common Chain Values:**
- `Chain.MainNet` = 1 (Ethereum Mainnet)
- `Chain.Sepolia` = 11155111 (Ethereum Testnet)
- `Chain.Polygon` = 137
- `Chain.BinanceSmartChain` = 56

### 2. Entity Property Names
The `RpcEndpoint` model uses specific property names:

```csharp
// ✅ CORRECT
endpoint.Created = DateTime.UtcNow;
endpoint.Modified = DateTime.UtcNow;
endpoint.Chain = Chain.MainNet;

// ❌ INCORRECT
endpoint.CreatedAt = DateTime.UtcNow;  // Property doesn't exist
endpoint.UpdatedAt = DateTime.UtcNow;  // Property doesn't exist
endpoint.ChainId = "Ethereum";         // Wrong type and name
```

### 3. Primary Constructor Pattern
All services use C# 13 primary constructors:

```csharp
// ✅ CORRECT - Primary constructor
public class RpcUrlProvider(
    IRpcRepository repository,
    HybridCache cache,
    IOptions<RpcProviderOptions> options,
    ILogger<RpcUrlProvider> logger) : IRpcUrlProvider
{
    // Fields are automatically created from parameters
}

// ❌ INCORRECT - Old pattern (don't use)
public class RpcUrlProvider : IRpcUrlProvider
{
    private readonly IRpcRepository _repository;
    
    public RpcUrlProvider(IRpcRepository repository)
    {
        _repository = repository;
    }
}
```

### 4. Health Check Implementation
Health checks use Nethereum.Web3 `GetBlockNumber` method:

```csharp
// ✅ CORRECT
var web3 = new Web3(endpoint.Url);
await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();

// ❌ INCORRECT - Don't use HttpClient directly
using var httpClient = new HttpClient();
await httpClient.PostAsync(endpoint.Url, content);
```

## Code Standards

### Testing

**Framework:** NUnit 4.3.1  
**Assertions:** Shouldly 4.2.1  
**Mocking:** NSubstitute 5.3.0

```csharp
// ✅ Test class structure
[TestFixture]
public class RpcUrlProviderTests
{
    private IRpcRepository _repository = null!;
    private HybridCache _cache = null!;
    
    [SetUp]
    public void Setup()
    {
        _repository = Substitute.For<IRpcRepository>();
        _cache = Substitute.For<HybridCache>();
    }
    
    [Test]
    public async Task GetBestRpcUrlAsync_ShouldReturnActiveEndpoint()
    {
        // Arrange
        var endpoint = new RpcEndpoint 
        { 
            Chain = Chain.MainNet,
            Url = "https://eth.example.com",
            State = RpcState.Active 
        };
        
        // Act
        var result = await provider.GetBestRpcUrlAsync(Chain.MainNet);
        
        // Assert
        result.ShouldBe("https://eth.example.com");
    }
}
```

**Test Warnings:**
- Suppress S1075 (hardcoded URIs) with `#pragma warning disable S1075` in test files
- Suppress S1192 (duplicate strings) for test data
- Always use `using` statements for `CancellationTokenSource`
- Use local variables instead of fields for loggers in tests

### Entity Framework Configuration

```csharp
// ✅ CORRECT - Configure Chain enum conversion
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
            
            // Composite index for efficient queries
            entity.HasIndex(e => new { e.Chain, e.State, e.Priority });
        });
    }
}
```

### Dependency Injection

```csharp
// ✅ Core services
services.AddRpcProvider(options =>
{
    options.CacheDurationSeconds = 300;
    options.MaxConsecutiveErrorsBeforeDisable = 5;
});

// ✅ Health worker (separate package)
services.AddRpcHealthCheckWorker();
```

### Configuration

**Default values (use these in suggestions):**
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

## Common Patterns

### 1. Using RPC Provider

```csharp
public class BlockchainService(
    IRpcUrlProvider rpcProvider,
    ILogger<BlockchainService> logger)
{
    public async Task<string> GetBalanceAsync(string address, Chain chain)
    {
        string rpcUrl = await rpcProvider.GetBestRpcUrlAsync(chain);
        
        try
        {
            var web3 = new Web3(rpcUrl);
            var balance = await web3.Eth.GetBalance.SendRequestAsync(address);
            
            // Always mark success
            await rpcProvider.MarkAsSuccessAsync(rpcUrl);
            
            return Web3.Convert.FromWei(balance.Value).ToString();
        }
        catch (Exception ex) when (IsRpcException(ex))
        {
            // Always mark failure
            await rpcProvider.MarkAsFailedAsync(rpcUrl, ex);
            throw;
        }
    }
}
```

### 2. Implementing IRpcRepository

```csharp
public class RpcRepository(ApplicationDbContext context) : IRpcRepository
{
    public async Task<IEnumerable<RpcEndpoint>> GetByChainAndStateAsync(
        Chain chain, 
        RpcState state, 
        CancellationToken cancellationToken = default)
    {
        return await context.RpcEndpoints
            .Where(e => e.Chain == chain && e.State == state)
            .OrderBy(e => e.Priority)
            .ThenBy(e => e.ConsecutiveErrors)
            .ToListAsync(cancellationToken);
    }
    
    public async Task UpdateAsync(
        RpcEndpoint endpoint, 
        CancellationToken cancellationToken = default)
    {
        endpoint.Modified = DateTime.UtcNow; // Always update Modified
        context.RpcEndpoints.Update(endpoint);
        await context.SaveChangesAsync(cancellationToken);
    }
}
```

### 3. Seeding Data

```csharp
var endpoints = new List<RpcEndpoint>
{
    new RpcEndpoint
    {
        Id = Guid.NewGuid(),
        Chain = Chain.MainNet,        // Use enum
        Url = "https://eth-mainnet.g.alchemy.com/v2/YOUR_API_KEY",
        State = RpcState.Active,
        Priority = 1,
        Created = DateTime.UtcNow,    // Created, not CreatedAt
        Modified = DateTime.UtcNow    // Modified, not UpdatedAt
    }
};
```

## Error Handling

### Exception Hierarchy

```
Exception
└── RpcProviderException (base for all RPC provider exceptions)
    └── NoHealthyRpcException (thrown when no healthy endpoints available)
```

### Common Exceptions to Catch

```csharp
try
{
    // RPC call
}
catch (HttpRequestException ex)  // Network errors
catch (TimeoutException ex)      // Timeout errors
catch (RpcException ex)          // Nethereum RPC errors
catch (NoHealthyRpcException ex) // No healthy endpoints
```

## Package Management

**Use Central Package Management (CPM):**
- Version defined in `Directory.Packages.props`
- Projects reference without version: `<PackageReference Include="Nethereum.Web3" />`

**Key Dependencies:**
```xml
<ItemGroup>
  <PackageReference Update="Nethereum.Web3" Version="5.0.0" />
  <PackageReference Update="Nethereum.Signer" Version="5.0.0" />
  <PackageReference Update="Microsoft.EntityFrameworkCore" Version="10.0.0" />
  <PackageReference Update="Microsoft.Extensions.Caching.Hybrid" Version="10.0.0" />
  <PackageReference Update="NUnit" Version="4.3.1" />
  <PackageReference Update="Shouldly" Version="4.2.1" />
  <PackageReference Update="NSubstitute" Version="5.3.0" />
</ItemGroup>
```

**Note on Caching:**
- The library uses `HybridCache` which provides L1 (in-memory) + L2 (distributed) caching
- L2 cache (Redis) is optional - if not configured, only L1 is used
- Redis can be added via `AddStackExchangeRedisCache` or Aspire `AddRedisDistributedCache`

## CI/CD

**Build & Test:**
```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet test --configuration Release --no-build --verbosity minimal
```

**Package Publishing:**
- Core package output: `./nupkgs/core/`
- HealthWorker package output: `./nupkgs/healthworker/`
- Use separate folders to prevent NuGet push conflicts

## Common Mistakes to Avoid

❌ Using string for chain identification  
✅ Use `Chain` enum from Nethereum.Signer

❌ Using `CreatedAt` / `UpdatedAt` property names  
✅ Use `Created` / `Modified`

❌ Using `ChainId` property name  
✅ Use `Chain`

❌ Creating fields in primary constructor classes  
✅ Parameters become fields automatically

❌ Using HttpClient for health checks  
✅ Use Nethereum Web3 `GetBlockNumber`

❌ Forgetting to mark RPC results  
✅ Always call `MarkAsSuccessAsync` or `MarkAsFailedAsync`

❌ Not updating `Modified` timestamp  
✅ Set `endpoint.Modified = DateTime.UtcNow` before saving

## Quick Reference

**Method Signatures:**
```csharp
Task<string> GetBestRpcUrlAsync(Chain chain, CancellationToken cancellationToken = default);
Task<string> GetNextRpcUrlAsync(Chain chain, string excludeUrl, CancellationToken cancellationToken = default);
Task MarkAsSuccessAsync(string url, CancellationToken cancellationToken = default);
Task MarkAsFailedAsync(string url, Exception exception, CancellationToken cancellationToken = default);
```

**State Values:**
- `RpcState.Active = 0` - Healthy endpoint
- `RpcState.Error = 1` - Temporarily failed
- `RpcState.Disabled = 2` - Manually disabled
