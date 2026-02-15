# ExecuteWithRetry Usage Examples

This guide shows how to use the new `ExecuteWithRetryAsync` methods for automatic RPC failover.

## Basic Usage


```csharp
using Nethereum.Signer;
using Nethereum.Web3;
using RpcProvider.Interfaces;

public class WalletService
{
    private readonly IRpcUrlProvider _rpcProvider;

    public WalletService(IRpcUrlProvider rpcProvider)
    {
        _rpcProvider = rpcProvider;
    }

    public async Task<decimal> GetBalanceAsync(string address)
    {
        // Automatically tries all available RPC endpoints until success
        var balance = await _rpcProvider.ExecuteWithRetryAsync(
            Chain.MainNet,
            async (rpcUrl, ct) =>
            {
                var web3 = new Web3(rpcUrl);
                var balanceWei = await web3.Eth.GetBalance.SendRequestAsync(address);
                return Web3.Convert.FromWei(balanceWei.Value);
            });

        return balance;
    }
}
```

### Example 1: Get Balance with Auto-Retry


```csharp
using Nethereum.Signer;
using Nethereum.Web3;
using RpcProvider.Interfaces;

public class WalletService
{
    private readonly IRpcUrlProvider _rpcProvider;

    public WalletService(IRpcUrlProvider rpcProvider)
    {
        _rpcProvider = rpcProvider;
    }

    public async Task<decimal> GetBalanceAsync(string address)
    {
        // Automatically tries all available RPC endpoints until success
        var balance = await _rpcProvider.ExecuteWithRetryAsync(
            Chain.MainNet,
            async (rpcUrl, ct) =>
            {
                var web3 = new Web3(rpcUrl);
                var balanceWei = await web3.Eth.GetBalance.SendRequestAsync(address);
                return Web3.Convert.FromWei(balanceWei.Value);
            });

        return balance;
    }
}
```

### Example 2: Get Block Number

```csharp
public async Task<ulong> GetCurrentBlockNumberAsync(Chain chain)
{
    var blockNumber = await _rpcProvider.ExecuteWithRetryAsync(
        chain,
        async (rpcUrl, ct) =>
        {
            var web3 = new Web3(rpcUrl);
            var block = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            return (ulong)block.Value;
        });

    return blockNumber;
}
```

### Example 3: Send Transaction

```csharp
public async Task<string> SendTransactionAsync(
    Chain chain,
    string to,
    decimal amount)
{
    var txHash = await _rpcProvider.ExecuteWithRetryAsync(
        chain,
        async (rpcUrl, ct) =>
        {
            var web3 = new Web3(rpcUrl);
            var amountWei = Web3.Convert.ToWei(amount);
            
            var transaction = await web3.Eth.GetEtherTransferService()
                .TransferEtherAndWaitForReceiptAsync(to, amountWei);
                
            return transaction.TransactionHash;
        });

    return txHash;
}
```

## Advanced Usage

### Example 4: Ignore Backoff for Critical Operations

```csharp
public async Task<string> GetCriticalDataAsync(Chain chain, string contractAddress)
{
    // Force immediate retry of all endpoints, even those in backoff period
    var data = await _rpcProvider.ExecuteWithRetryAsync(
        chain,
        async (rpcUrl, ct) =>
        {
            var web3 = new Web3(rpcUrl);
            var contract = web3.Eth.GetContract(abi, contractAddress);
            var function = contract.GetFunction("getData");
            return await function.CallAsync<string>();
        },
        ignoreBackoff: true); // ← Ignores exponential backoff

    return data;
}
```

### Example 5: Void Operations (No Return Value)

```csharp
public async Task LogCurrentBlockAsync(Chain chain)
{
    await _rpcProvider.ExecuteWithRetryAsync(
        chain,
        async (rpcUrl, ct) =>
        {
            var web3 = new Web3(rpcUrl);
            var block = await web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            Console.WriteLine($"Current block: {block.Value}");
        });
}
```

### Example 6: With Cancellation Token

```csharp
public async Task<string> GetDataWithTimeoutAsync(
    Chain chain,
    CancellationToken cancellationToken)
{
    var data = await _rpcProvider.ExecuteWithRetryAsync(
        chain,
        async (rpcUrl, ct) =>
        {
            var web3 = new Web3(rpcUrl);
            // Your blockchain operation here
            return await SomeBlockchainOperationAsync(web3, ct);
        },
        cancellationToken: cancellationToken);

    return data;
}
```

## Error Handling

### Example 7: Handle All Endpoints Failed

```csharp
using RpcProvider.Exceptions;

public async Task<decimal> GetBalanceWithErrorHandlingAsync(string address)
{
    try
    {
        return await _rpcProvider.ExecuteWithRetryAsync(
            Chain.MainNet,
            async (rpcUrl, ct) =>
            {
                var web3 = new Web3(rpcUrl);
                var balanceWei = await web3.Eth.GetBalance.SendRequestAsync(address);
                return Web3.Convert.FromWei(balanceWei.Value);
            });
    }
    catch (AllRpcEndpointsFailedException ex)
    {
        // All available RPC endpoints failed
        Console.WriteLine($"All {ex.Failures.Count} endpoints failed for chain {ex.Chain}");
        
        // Log each failure
        foreach (var failure in ex.Failures)
        {
            Console.WriteLine($"  - {failure.Url}: {failure.Exception.Message}");
        }
        
        throw;
    }
}
```

## Configuration

### Example 8: Configure Max Retry Attempts

```json
{
  "RpcProvider": {
    "MaxRetryAttempts": 3,
    "CacheDurationSeconds": 300,
    "MaxConsecutiveErrorsBeforeDisable": 5,
    "BaseBackoffMinutes": 1,
    "MaxBackoffMinutes": 30,
    "AllowDisabledEndpointsAsFallback": false
  }
}
```

```csharp
services.AddRpcProvider(options =>
{
    options.MaxRetryAttempts = 3; // Try max 3 endpoints (-1 = try all)
    options.AllowDisabledEndpointsAsFallback = true; // Use disabled as last resort
});
```

## How It Works

### Retry Order

1. **Active Endpoints** (RpcState.Active) - Healthy endpoints, ordered by priority
2. **Error Endpoints (Backoff Passed)** - Error endpoints that passed exponential backoff
3. **Error Endpoints (All)** - If `ignoreBackoff: true`, includes all error endpoints
4. **Disabled Endpoints** - If `AllowDisabledEndpointsAsFallback: true`

### Automatic Tracking

The method automatically:
- ✅ Marks successful endpoints (resets error count)
- ❌ Marks failed endpoints (increments error count)
- 🔄 Retries with next available endpoint
- 💾 Invalidates cache when state changes
- 📊 Logs all attempts and failures

### Exponential Backoff

Failed endpoints enter exponential backoff:
```
Error #1 → 1 minute
Error #2 → 2 minutes
Error #3 → 4 minutes
Error #4 → 8 minutes
Error #5 → 16 minutes (becomes RpcState.Error)
Error #6+ → 30 minutes (capped)
```

## Real-World Example

### Example 9: Complete Service Implementation

```csharp
public class BlockchainDataService
{
    private readonly IRpcUrlProvider _rpcProvider;
    private readonly ILogger<BlockchainDataService> _logger;

    public BlockchainDataService(
        IRpcUrlProvider rpcProvider,
        ILogger<BlockchainDataService> logger)
    {
        _rpcProvider = rpcProvider;
        _logger = logger;
    }

    public async Task<TokenBalance> GetTokenBalanceAsync(
        Chain chain,
        string walletAddress,
        string tokenContractAddress,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var balance = await _rpcProvider.ExecuteWithRetryAsync(
                chain,
                async (rpcUrl, ct) =>
                {
                    _logger.LogDebug("Querying token balance using RPC: {RpcUrl}", rpcUrl);
                    
                    var web3 = new Web3(rpcUrl);
                    var contract = web3.Eth.GetContract(ERC20_ABI, tokenContractAddress);
                    
                    var balanceFunction = contract.GetFunction("balanceOf");
                    var decimalsFunction = contract.GetFunction("decimals");
                    var symbolFunction = contract.GetFunction("symbol");
                    
                    var balanceWei = await balanceFunction.CallAsync<BigInteger>(walletAddress);
                    var decimals = await decimalsFunction.CallAsync<int>();
                    var symbol = await symbolFunction.CallAsync<string>();
                    
                    var balanceDecimal = (decimal)balanceWei / (decimal)Math.Pow(10, decimals);
                    
                    return new TokenBalance
                    {
                        Symbol = symbol,
                        Balance = balanceDecimal,
                        Chain = chain
                    };
                },
                cancellationToken: cancellationToken);

            _logger.LogInformation(
                "Successfully retrieved token balance: {Balance} {Symbol}",
                balance.Balance,
                balance.Symbol);

            return balance;
        }
        catch (AllRpcEndpointsFailedException ex)
        {
            _logger.LogError(
                ex,
                "Failed to get token balance after trying {Count} endpoints",
                ex.Failures.Count);
            throw;
        }
    }
}

public record TokenBalance
{
    public string Symbol { get; init; } = string.Empty;
    public decimal Balance { get; init; }
    public Chain Chain { get; init; }
}
```

## Benefits

1. **Automatic Failover** - No manual retry logic needed
2. **Smart Backoff** - Prevents overwhelming failed endpoints
3. **Transparent Tracking** - Endpoint states updated automatically
4. **Flexible Configuration** - Control retry behavior per operation
5. **Comprehensive Logging** - Full visibility into retry attempts
6. **Type Safe** - Strongly typed operations with generics
7. **Cancellation Support** - Respects cancellation tokens
