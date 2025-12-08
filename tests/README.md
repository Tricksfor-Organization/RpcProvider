# RpcProvider Test Suite

## Overview

Comprehensive unit test suite for the RpcProvider solution using **NUnit**, **Shouldly**, and **NSubstitute**.

## Test Projects

### RpcProvider.Core.Tests
Tests for the core functionality of the RPC provider library.

**Test Coverage: 42 tests**

#### Configuration Tests (4 tests)
- `RpcProviderOptionsTests` - Tests for configuration options
  - Default values validation
  - Custom values storage
  - Zero values handling
  - Boolean flags toggling

#### Exception Tests (7 tests)
- `NoHealthyRpcExceptionTests` - Tests for NoHealthyRpcException
  - Constructor variations
  - Chain property handling
  - Exception inheritance
- `RpcProviderExceptionTests` - Tests for base RpcProviderException
  - Message handling
  - Inner exception support

#### Model Tests (7 tests)
- `RpcEndpointTests` - Tests for RpcEndpoint model
  - Property initialization
  - Value storage
  - Different chains handling
- `RpcStateTests` - Tests for RpcState enum
  - Enum values
  - String conversion
  - Comparability

#### Service Tests (24 tests)
- `RpcUrlProviderTests` - Tests for RpcUrlProvider service
  - Cache retrieval
  - Best endpoint selection
  - Failover logic
  - Error state handling
  - Disabled endpoints fallback
  - Health checking
  - Exception handling
- `RpcHealthCheckerTests` - Tests for RpcHealthChecker service
  - Error endpoint checking
  - Endpoint recovery
  - Exception handling
  - Repository failures

### RpcProvider.HealthWorker.Tests
Tests for the health check background worker.

**Test Coverage: 6 tests**

#### Worker Tests (6 tests)
- `RpcHealthCheckWorkerTests` - Tests for RpcHealthCheckWorker
  - Constructor validation
  - Disabled health checks behavior
  - Cancellation handling

## Test Frameworks & Libraries

- **NUnit 4.3.1** - Test framework
- **Shouldly 4.2.1** - Assertion library
- **NSubstitute 5.3.0** - Mocking library
- **coverlet.collector 6.0.2** - Code coverage collection

## Running Tests

### Run all tests
```bash
dotnet test
```

### Run specific test project
```bash
dotnet test tests/RpcProvider.Core.Tests/RpcProvider.Core.Tests.csproj
dotnet test tests/RpcProvider.HealthWorker.Tests/RpcProvider.HealthWorker.Tests.csproj
```

### Run with code coverage
```bash
dotnet test --collect:"XPlat Code Coverage"
```

### Run with detailed output
```bash
dotnet test --verbosity normal
```

## Test Summary

| Project | Tests | Status |
|---------|-------|--------|
| RpcProvider.Core.Tests | 42 | ✅ All Passing |
| RpcProvider.HealthWorker.Tests | 6 | ✅ All Passing |
| **Total** | **48** | **✅ All Passing** |

## Key Testing Patterns

### Mocking with NSubstitute
```csharp
var repository = Substitute.For<IRpcRepository>();
repository.GetByChainAsync(chain, Arg.Any<CancellationToken>())
    .Returns(endpoints);
```

### Assertions with Shouldly
```csharp
result.ShouldBe(expectedValue);
result.ShouldNotBeNull();
exception.ShouldBeOfType<NoHealthyRpcException>();
```

### Async Testing
```csharp
[Test]
public async Task GetBestRpcUrlAsync_WhenCachedUrlExists_ShouldReturnCachedUrl()
{
    // Arrange, Act, Assert
}
```

## Notes

- Tests use `Chain.MainNet` and `Chain.Sepolia` from Nethereum.Signer package
- RpcHealthChecker tests are limited as Nethereum.Web3 calls are difficult to mock
- All tests follow AAA (Arrange-Act-Assert) pattern
- Tests are independent and can run in any order
