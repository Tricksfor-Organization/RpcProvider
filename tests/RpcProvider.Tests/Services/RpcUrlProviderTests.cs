using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Signer;
using NSubstitute.ExceptionExtensions;
using RpcProvider.Configuration;
using RpcProvider.Exceptions;
using RpcProvider.Interfaces;
using RpcProvider.Models;
using RpcProvider.Services;

namespace RpcProvider.Tests.Services;

[TestFixture]
public class RpcUrlProviderTests
{
    private IRpcRepository _repository = null!;
    private HybridCache _cache = null!;
    private RpcProviderOptions _options = null!;
    private RpcUrlProvider _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IRpcRepository>();
        _cache = Substitute.For<HybridCache>();
        var logger = Substitute.For<ILogger<RpcUrlProvider>>();
        _options = new RpcProviderOptions
        {
            CacheDurationSeconds = 300,
            MaxConsecutiveErrorsBeforeDisable = 5,
            RequestTimeoutSeconds = 30,
            AllowDisabledEndpointsAsFallback = false,
            BaseBackoffMinutes = 1,
            MaxBackoffMinutes = 60,
            EnableHealthChecks = true,
            HealthCheckIntervalMinutes = 5
        };

        var optionsWrapper = Substitute.For<IOptions<RpcProviderOptions>>();
        optionsWrapper.Value.Returns(_options);

        _sut = new RpcUrlProvider(_repository, _cache, optionsWrapper, logger);
    }

    [Test]
    public async Task GetBestRpcUrlAsync_WhenCachedUrlExists_ShouldReturnCachedUrl()
    {
        // Arrange
        var chain = Chain.MainNet;
        var cachedUrl = "https://cached-eth-rpc.example.com";
        var cacheKey = $"rpc:best:{(int)chain}";

        _cache.GetOrCreateAsync<string?>(
                cacheKey,
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(cachedUrl);

        // Act
        var result = await _sut.GetBestRpcUrlAsync(chain);

        // Assert
        result.ShouldBe(cachedUrl);
        await _repository.DidNotReceive().GetByChainAndStateAsync(Arg.Any<Chain>(), Arg.Any<RpcState>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetBestRpcUrlAsync_WhenNoCacheAndActiveEndpointsExist_ShouldReturnBestActiveEndpoint()
    {
        // Arrange
        var chain = Chain.MainNet;
        var endpoints = new List<RpcEndpoint>
        {
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-1.example.com", State = RpcState.Active, Priority = 2, ConsecutiveErrors = 0 },
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-2.example.com", State = RpcState.Active, Priority = 1, ConsecutiveErrors = 0 },
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-3.example.com", State = RpcState.Active, Priority = 1, ConsecutiveErrors = 1 }
        };

        _cache.GetOrCreateAsync<string?>(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns((string?)null);

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(endpoints);

        // Act
        var result = await _sut.GetBestRpcUrlAsync(chain);

        // Assert
        result.ShouldBe("https://eth-rpc-2.example.com"); // Priority 1, ConsecutiveErrors 0
        await _cache.Received(1).SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<HybridCacheEntryOptions>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetBestRpcUrlAsync_WhenNoActiveEndpoints_ShouldTryRecoverableErrorEndpoints()
    {
        // Arrange
        var chain = Chain.MainNet;
        var errorEndpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-error.example.com",
            State = RpcState.Error,
            Priority = 1,
            ConsecutiveErrors = 3,
            LastErrorAt = DateTime.UtcNow.AddMinutes(-10) // Past backoff period
        };

        _cache.GetOrCreateAsync<string?>(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns((string?)null);

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());

        _repository.GetByChainAndStateAsync(chain, RpcState.Error, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { errorEndpoint });

        // Act
        var result = await _sut.GetBestRpcUrlAsync(chain);

        // Assert
        result.ShouldBe("https://eth-rpc-error.example.com");
    }

    [Test]
    public async Task GetBestRpcUrlAsync_WhenAllowDisabledFallbackEnabled_ShouldReturnDisabledEndpoint()
    {
        // Arrange
        var chain = Chain.MainNet;
        _options.AllowDisabledEndpointsAsFallback = true;
        
        var disabledEndpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-disabled.example.com",
            State = RpcState.Disabled,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        _cache.GetOrCreateAsync<string?>(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns((string?)null);

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());

        _repository.GetByChainAndStateAsync(chain, RpcState.Error, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());

        _repository.GetByChainAndStateAsync(chain, RpcState.Disabled, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { disabledEndpoint });

        // Act
        var result = await _sut.GetBestRpcUrlAsync(chain);

        // Assert
        result.ShouldBe("https://eth-rpc-disabled.example.com");
    }

    [Test]
    public void GetBestRpcUrlAsync_WhenNoEndpointsAvailable_ShouldThrowNoHealthyRpcException()
    {
        // Arrange
        var chain = Chain.MainNet;

        _cache.GetOrCreateAsync<string?>(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns((string?)null);

        _repository.GetByChainAndStateAsync(Arg.Any<Chain>(), Arg.Any<RpcState>(), Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());

        // Act & Assert
        Should.ThrowAsync<NoHealthyRpcException>(async () => await _sut.GetBestRpcUrlAsync(chain));
    }

    [Test]
    public async Task GetNextRpcUrlAsync_WhenAlternativeEndpointsExist_ShouldReturnNextBestEndpoint()
    {
        // Arrange
        var chain = Chain.MainNet;
        var failedUrl = "https://eth-rpc-failed.example.com";
        var endpoints = new List<RpcEndpoint>
        {
            new() { Id = Guid.NewGuid(), Chain = chain, Url = failedUrl, State = RpcState.Active, Priority = 1, ConsecutiveErrors = 0 },
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-2.example.com", State = RpcState.Active, Priority = 2, ConsecutiveErrors = 0 },
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-3.example.com", State = RpcState.Active, Priority = 3, ConsecutiveErrors = 0 }
        };

        _repository.GetByChainAsync(chain, Arg.Any<CancellationToken>())
            .Returns(endpoints);

        // Act
        var result = await _sut.GetNextRpcUrlAsync(chain, failedUrl);

        // Assert
        result.ShouldBe("https://eth-rpc-2.example.com");
        await _cache.Received(1).SetAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<HybridCacheEntryOptions>(), Arg.Any<string[]>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public void GetNextRpcUrlAsync_WhenNoAlternativeEndpoints_ShouldThrowNoHealthyRpcException()
    {
        // Arrange
        var chain = Chain.MainNet;
        var failedUrl = "https://eth-rpc-failed.example.com";
        var endpoints = new List<RpcEndpoint>
        {
            new() { Id = Guid.NewGuid(), Chain = chain, Url = failedUrl, State = RpcState.Active, Priority = 1, ConsecutiveErrors = 0 }
        };

        _repository.GetByChainAsync(chain, Arg.Any<CancellationToken>())
            .Returns(endpoints);

        // Act & Assert
        Should.ThrowAsync<NoHealthyRpcException>(async () => await _sut.GetNextRpcUrlAsync(chain, failedUrl));
    }

    [Test]
    public void GetNextRpcUrlAsync_WhenFailedUrlIsNullOrEmpty_ShouldThrowArgumentException()
    {
        // Arrange
        var chain = Chain.MainNet;

        // Act & Assert
        Should.Throw<ArgumentException>(async () => await _sut.GetNextRpcUrlAsync(chain, string.Empty));
        Should.Throw<ArgumentException>(async () => await _sut.GetNextRpcUrlAsync(chain, null!));
    }

    [Test]
    public async Task MarkAsFailedAsync_WhenEndpointExists_ShouldIncrementErrorCount()
    {
        // Arrange
        var url = "https://eth-rpc.example.com";
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = Chain.MainNet,
            Url = url,
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 2
        };

        var exception = new Exception("Connection timeout");

        _repository.GetByUrlAsync(url, Arg.Any<CancellationToken>())
            .Returns(endpoint);

        // Act
        await _sut.MarkAsFailedAsync(url, exception);

        // Assert
        endpoint.ConsecutiveErrors.ShouldBe(3);
        endpoint.ErrorMessage.ShouldBe("Connection timeout");
        endpoint.LastErrorAt.ShouldNotBeNull();
        endpoint.State.ShouldBe(RpcState.Active); // Not yet at threshold
        await _repository.Received(1).UpdateAsync(endpoint, Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkAsFailedAsync_WhenThresholdExceeded_ShouldMarkAsError()
    {
        // Arrange
        var url = "https://eth-rpc.example.com";
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = Chain.MainNet,
            Url = url,
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 4 // One less than threshold
        };

        var exception = new Exception("Connection failed");

        _repository.GetByUrlAsync(url, Arg.Any<CancellationToken>())
            .Returns(endpoint);

        // Act
        await _sut.MarkAsFailedAsync(url, exception);

        // Assert
        endpoint.ConsecutiveErrors.ShouldBe(5);
        endpoint.State.ShouldBe(RpcState.Error);
        await _repository.Received(1).UpdateAsync(endpoint, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkAsFailedAsync_WhenEndpointDoesNotExist_ShouldNotThrow()
    {
        // Arrange
        var url = "https://non-existent-rpc.example.com";
        var exception = new Exception("Test error");

        _repository.GetByUrlAsync(url, Arg.Any<CancellationToken>())
            .Returns((RpcEndpoint?)null);

        // Act & Assert
        await Should.NotThrowAsync(() => _sut.MarkAsFailedAsync(url, exception));
        await _repository.DidNotReceive().UpdateAsync(Arg.Any<RpcEndpoint>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkAsSuccessAsync_WhenEndpointInErrorState_ShouldResetAndMarkActive()
    {
        // Arrange
        var url = "https://eth-rpc.example.com";
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = Chain.MainNet,
            Url = url,
            State = RpcState.Error,
            Priority = 1,
            ConsecutiveErrors = 5,
            ErrorMessage = "Previous error"
        };

        _repository.GetByUrlAsync(url, Arg.Any<CancellationToken>())
            .Returns(endpoint);

        // Act
        await _sut.MarkAsSuccessAsync(url);

        // Assert
        endpoint.ConsecutiveErrors.ShouldBe(0);
        endpoint.ErrorMessage.ShouldBeNull();
        endpoint.State.ShouldBe(RpcState.Active);
        await _repository.Received(1).UpdateAsync(endpoint, Arg.Any<CancellationToken>());
        await _cache.Received(1).RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkAsSuccessAsync_WhenEndpointAlreadyActive_ShouldResetErrors()
    {
        // Arrange
        var url = "https://eth-rpc.example.com";
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = Chain.MainNet,
            Url = url,
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 2,
            ErrorMessage = "Temporary error"
        };

        _repository.GetByUrlAsync(url, Arg.Any<CancellationToken>())
            .Returns(endpoint);

        // Act
        await _sut.MarkAsSuccessAsync(url);

        // Assert
        endpoint.ConsecutiveErrors.ShouldBe(0);
        endpoint.ErrorMessage.ShouldBeNull();
        endpoint.State.ShouldBe(RpcState.Active);
        await _repository.Received(1).UpdateAsync(endpoint, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetEndpointsByChainAsync_ShouldReturnAllEndpointsForChain()
    {
        // Arrange
        var chain = Chain.MainNet;
        var endpoints = new List<RpcEndpoint>
        {
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-1.example.com", State = RpcState.Active, Priority = 1 },
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-2.example.com", State = RpcState.Error, Priority = 2 },
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-3.example.com", State = RpcState.Disabled, Priority = 3 }
        };

        _repository.GetByChainAsync(chain, Arg.Any<CancellationToken>())
            .Returns(endpoints);

        // Act
        var result = await _sut.GetEndpointsByChainAsync(chain);

        // Assert
        result.ShouldNotBeNull();
        result.Count().ShouldBe(3);
    }

    [Test]
    public async Task IsHealthyAsync_WhenEndpointIsActiveWithNoErrors_ShouldReturnTrue()
    {
        // Arrange
        var url = "https://eth-rpc.example.com";
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = Chain.MainNet,
            Url = url,
            State = RpcState.Active,
            ConsecutiveErrors = 0
        };

        _repository.GetByUrlAsync(url, Arg.Any<CancellationToken>())
            .Returns(endpoint);

        // Act
        var result = await _sut.IsHealthyAsync(url);

        // Assert
        result.ShouldBeTrue();
    }

    [Test]
    public async Task IsHealthyAsync_WhenEndpointIsInErrorState_ShouldReturnFalse()
    {
        // Arrange
        var url = "https://eth-rpc.example.com";
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = Chain.MainNet,
            Url = url,
            State = RpcState.Error,
            ConsecutiveErrors = 5
        };

        _repository.GetByUrlAsync(url, Arg.Any<CancellationToken>())
            .Returns(endpoint);

        // Act
        var result = await _sut.IsHealthyAsync(url);

        // Assert
        result.ShouldBeFalse();
    }

    [Test]
    public async Task IsHealthyAsync_WhenEndpointHasErrors_ShouldReturnFalse()
    {
        // Arrange
        var url = "https://eth-rpc.example.com";
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = Chain.MainNet,
            Url = url,
            State = RpcState.Active,
            ConsecutiveErrors = 2
        };

        _repository.GetByUrlAsync(url, Arg.Any<CancellationToken>())
            .Returns(endpoint);

        // Act
        var result = await _sut.IsHealthyAsync(url);

        // Assert
        result.ShouldBeFalse();
    }

    [Test]
    public async Task IsHealthyAsync_WhenUrlIsNullOrEmpty_ShouldReturnFalse()
    {
        // Act
        var resultNull = await _sut.IsHealthyAsync(null!);
        var resultEmpty = await _sut.IsHealthyAsync(string.Empty);

        // Assert
        resultNull.ShouldBeFalse();
        resultEmpty.ShouldBeFalse();
    }

    [Test]
    public async Task GetBestRpcUrlAsync_WhenCacheThrowsException_ShouldContinueWithoutCache()
    {
        // Arrange
        var chain = Chain.MainNet;
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc.example.com",
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        _cache.GetOrCreateAsync<string?>(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Throws(new Exception("Cache error"));

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { endpoint });

        // Act
        var result = await _sut.GetBestRpcUrlAsync(chain);

        // Assert
        result.ShouldBe("https://eth-rpc.example.com");
    }

    [Test]
    public async Task GetBestRpcUrlAsync_WithCacheKeyPrefix_ShouldUsePrefixedCacheKey()
    {
        // Arrange
        var chain = Chain.MainNet;
        var cachedUrl = "https://cached-eth-rpc.example.com";
        var prefix = "ProjectA";
        var expectedCacheKey = $"rpc:best:{(int)chain}:{prefix}";
        
        _options.CacheKeyPrefix = prefix;

        _cache.GetOrCreateAsync<string?>(
                expectedCacheKey,
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(cachedUrl);

        // Act
        var result = await _sut.GetBestRpcUrlAsync(chain);

        // Assert
        result.ShouldBe(cachedUrl);
        await _cache.Received(1).GetOrCreateAsync<string?>(
            expectedCacheKey,
            Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task GetBestRpcUrlAsync_WithoutCacheKeyPrefix_ShouldUseDefaultCacheKey()
    {
        // Arrange
        var chain = Chain.MainNet;
        var cachedUrl = "https://cached-eth-rpc.example.com";
        var expectedCacheKey = $"rpc:best:{(int)chain}";
        
        _options.CacheKeyPrefix = null; // No prefix

        _cache.GetOrCreateAsync<string?>(
                expectedCacheKey,
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns(cachedUrl);

        // Act
        var result = await _sut.GetBestRpcUrlAsync(chain);

        // Assert
        result.ShouldBe(cachedUrl);
        await _cache.Received(1).GetOrCreateAsync<string?>(
            expectedCacheKey,
            Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
            cancellationToken: Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task MarkAsFailedAsync_WithCacheKeyPrefix_ShouldInvalidatePrefixedCache()
    {
        // Arrange
        var url = "https://eth-rpc.example.com";
        var chain = Chain.MainNet;
        var prefix = "ProjectB";
        var expectedCacheKey = $"rpc:best:{(int)chain}:{prefix}";
        
        _options.CacheKeyPrefix = prefix;
        
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = url,
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 2
        };

        var exception = new Exception("Connection timeout");

        _repository.GetByUrlAsync(url, Arg.Any<CancellationToken>())
            .Returns(endpoint);

        // Act
        await _sut.MarkAsFailedAsync(url, exception);

        // Assert
        await _cache.Received(1).RemoveAsync(expectedCacheKey, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task CacheEndpoint_WithCacheKeyPrefix_ShouldSetPrefixedCache()
    {
        // Arrange
        var chain = Chain.MainNet;
        var prefix = "ProjectC";
        var expectedCacheKey = $"rpc:best:{(int)chain}:{prefix}";
        
        _options.CacheKeyPrefix = prefix;
        
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-1.example.com",
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        _cache.GetOrCreateAsync<string?>(
                Arg.Any<string>(),
                Arg.Any<Func<CancellationToken, ValueTask<string?>>>(),
                cancellationToken: Arg.Any<CancellationToken>())
            .Returns((string?)null);

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { endpoint });

        // Act
        var result = await _sut.GetBestRpcUrlAsync(chain);

        // Assert
        result.ShouldBe(endpoint.Url);
        await _cache.Received(1).SetAsync(
            expectedCacheKey,
            endpoint.Url,
            Arg.Any<HybridCacheEntryOptions>(),
            Arg.Any<string[]>(),
            Arg.Any<CancellationToken>());
    }

    #region ExecuteWithRetryAsync Tests

    [Test]
    public async Task ExecuteWithRetryAsync_WithResult_WhenFirstEndpointSucceeds_ShouldReturnResult()
    {
        // Arrange
        var chain = Chain.MainNet;
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-1.example.com",
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { endpoint });
        _repository.GetByChainAndStateAsync(chain, RpcState.Error, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());

        var expectedResult = "0x1234567890";
        Func<string, CancellationToken, Task<string>> operation = (url, ct) =>
        {
            url.ShouldBe(endpoint.Url);
            return Task.FromResult(expectedResult);
        };

        // Act
        var result = await _sut.ExecuteWithRetryAsync(chain, operation);

        // Assert
        result.ShouldBe(expectedResult);
        await _repository.Received(1).GetByUrlAsync(endpoint.Url, Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteWithRetryAsync_WithResult_WhenFirstFails_ShouldRetryWithNextEndpoint()
    {
        // Arrange
        var chain = Chain.MainNet;
        var endpoint1 = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-1.example.com",
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        var endpoint2 = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-2.example.com",
            State = RpcState.Active,
            Priority = 2,
            ConsecutiveErrors = 0
        };

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { endpoint1, endpoint2 });
        _repository.GetByChainAndStateAsync(chain, RpcState.Error, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());
        
        _repository.GetByUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(endpoint1, endpoint2);

        var callCount = 0;
        var expectedResult = "0x1234567890";
        Func<string, CancellationToken, Task<string>> operation = (url, ct) =>
        {
            callCount++;
            if (callCount == 1)
            {
                url.ShouldBe(endpoint1.Url);
                throw new HttpRequestException("Network error");
            }
            url.ShouldBe(endpoint2.Url);
            return Task.FromResult(expectedResult);
        };

        // Act
        var result = await _sut.ExecuteWithRetryAsync(chain, operation);

        // Assert
        result.ShouldBe(expectedResult);
        callCount.ShouldBe(2);
        await _repository.Received(1).UpdateAsync(
            Arg.Is<RpcEndpoint>(e => e.Url == endpoint1.Url && e.ConsecutiveErrors == 1),
            Arg.Any<CancellationToken>());
        await _repository.Received(1).UpdateAsync(
            Arg.Is<RpcEndpoint>(e => e.Url == endpoint2.Url && e.ConsecutiveErrors == 0),
            Arg.Any<CancellationToken>());
    }

    [Test]
    public async Task ExecuteWithRetryAsync_WithResult_WhenAllEndpointsFail_ShouldThrowAllRpcEndpointsFailedException()
    {
        // Arrange
        var chain = Chain.MainNet;
        var endpoint1 = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-1.example.com",
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        var endpoint2 = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-2.example.com",
            State = RpcState.Active,
            Priority = 2,
            ConsecutiveErrors = 0
        };

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { endpoint1, endpoint2 });
        _repository.GetByChainAndStateAsync(chain, RpcState.Error, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());
        
        _repository.GetByUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(endpoint1, endpoint2);

        Func<string, CancellationToken, Task<string>> operation = (url, ct) =>
        {
            throw new HttpRequestException($"Network error for {url}");
        };

        // Act & Assert
        var exception = await Should.ThrowAsync<AllRpcEndpointsFailedException>(
            async () => await _sut.ExecuteWithRetryAsync(chain, operation));

        exception.Chain.ShouldBe(chain);
        exception.Failures.Count.ShouldBe(2);
        exception.Failures[0].Url.ShouldBe(endpoint1.Url);
        exception.Failures[1].Url.ShouldBe(endpoint2.Url);
    }

    [Test]
    public async Task ExecuteWithRetryAsync_WithResult_WhenIgnoreBackoff_ShouldTryAllErrorEndpoints()
    {
        // Arrange
        var chain = Chain.MainNet;
        var activeEndpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-active.example.com",
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        var errorEndpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-error.example.com",
            State = RpcState.Error,
            Priority = 2,
            ConsecutiveErrors = 5,
            LastErrorAt = DateTime.UtcNow.AddSeconds(-30) // Still in backoff period
        };

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { activeEndpoint });
        _repository.GetByChainAndStateAsync(chain, RpcState.Error, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { errorEndpoint });
        
        _repository.GetByUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(activeEndpoint, errorEndpoint);

        var callCount = 0;
        var expectedResult = "0x1234567890";
        Func<string, CancellationToken, Task<string>> operation = (url, ct) =>
        {
            callCount++;
            if (callCount == 1)
            {
                url.ShouldBe(activeEndpoint.Url);
                throw new HttpRequestException("Network error");
            }
            url.ShouldBe(errorEndpoint.Url);
            return Task.FromResult(expectedResult);
        };

        // Act
        var result = await _sut.ExecuteWithRetryAsync(chain, operation, ignoreBackoff: true);

        // Assert
        result.ShouldBe(expectedResult);
        callCount.ShouldBe(2);
    }

    [Test]
    public async Task ExecuteWithRetryAsync_WithResult_WhenMaxRetryAttemptsReached_ShouldStopRetrying()
    {
        // Arrange
        var chain = Chain.MainNet;
        _options.MaxRetryAttempts = 2;

        var endpoints = new List<RpcEndpoint>
        {
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-1.example.com", State = RpcState.Active, Priority = 1 },
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-2.example.com", State = RpcState.Active, Priority = 2 },
            new() { Id = Guid.NewGuid(), Chain = chain, Url = "https://eth-rpc-3.example.com", State = RpcState.Active, Priority = 3 }
        };

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(endpoints);
        _repository.GetByChainAndStateAsync(chain, RpcState.Error, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());
        
        _repository.GetByUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(endpoints[0], endpoints[1], endpoints[2]);

        var callCount = 0;
        Func<string, CancellationToken, Task<string>> operation = (url, ct) =>
        {
            callCount++;
            throw new HttpRequestException($"Network error for {url}");
        };

        // Act & Assert
        var exception = await Should.ThrowAsync<AllRpcEndpointsFailedException>(
            async () => await _sut.ExecuteWithRetryAsync(chain, operation));

        callCount.ShouldBe(2); // Should only try 2 times, not 3
        exception.Failures.Count.ShouldBe(2);
    }

    [Test]
    public async Task ExecuteWithRetryAsync_WithResult_WhenOperationIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var chain = Chain.MainNet;

        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(
            async () => await _sut.ExecuteWithRetryAsync<string>(chain, null!));
    }

    [Test]
    public async Task ExecuteWithRetryAsync_WithoutResult_ShouldExecuteSuccessfully()
    {
        // Arrange
        var chain = Chain.MainNet;
        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-1.example.com",
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { endpoint });
        _repository.GetByChainAndStateAsync(chain, RpcState.Error, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());
        
        _repository.GetByUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(endpoint);

        var executed = false;
        Func<string, CancellationToken, Task> operation = (url, ct) =>
        {
            url.ShouldBe(endpoint.Url);
            executed = true;
            return Task.CompletedTask;
        };

        // Act
        await _sut.ExecuteWithRetryAsync(chain, operation);

        // Assert
        executed.ShouldBeTrue();
    }

    [Test]
    public async Task ExecuteWithRetryAsync_WithoutResult_WhenFirstFails_ShouldRetryWithNextEndpoint()
    {
        // Arrange
        var chain = Chain.MainNet;
        var endpoint1 = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-1.example.com",
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        var endpoint2 = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-2.example.com",
            State = RpcState.Active,
            Priority = 2,
            ConsecutiveErrors = 0
        };

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { endpoint1, endpoint2 });
        _repository.GetByChainAndStateAsync(chain, RpcState.Error, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());
        
        _repository.GetByUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(endpoint1, endpoint2);

        var callCount = 0;
        Func<string, CancellationToken, Task> operation = (url, ct) =>
        {
            callCount++;
            if (callCount == 1)
            {
                url.ShouldBe(endpoint1.Url);
                throw new HttpRequestException("Network error");
            }
            url.ShouldBe(endpoint2.Url);
            return Task.CompletedTask;
        };

        // Act
        await _sut.ExecuteWithRetryAsync(chain, operation);

        // Assert
        callCount.ShouldBe(2);
    }

    [Test]
    public async Task ExecuteWithRetryAsync_WithoutResult_WhenOperationIsNull_ShouldThrowArgumentNullException()
    {
        // Arrange
        var chain = Chain.MainNet;

        // Act & Assert
        await Should.ThrowAsync<ArgumentNullException>(
            async () => await _sut.ExecuteWithRetryAsync(chain, (Func<string, CancellationToken, Task>)null!));
    }

    [Test]
    public async Task ExecuteWithRetryAsync_WithDisabledFallback_ShouldTryDisabledEndpoints()
    {
        // Arrange
        var chain = Chain.MainNet;
        _options.AllowDisabledEndpointsAsFallback = true;

        var activeEndpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-active.example.com",
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        var disabledEndpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-disabled.example.com",
            State = RpcState.Disabled,
            Priority = 2
        };

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { activeEndpoint });
        _repository.GetByChainAndStateAsync(chain, RpcState.Error, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint>());
        _repository.GetByChainAndStateAsync(chain, RpcState.Disabled, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { disabledEndpoint });
        
        _repository.GetByUrlAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(activeEndpoint, disabledEndpoint);

        var callCount = 0;
        var expectedResult = "0x1234567890";
        Func<string, CancellationToken, Task<string>> operation = (url, ct) =>
        {
            callCount++;
            if (callCount == 1)
            {
                url.ShouldBe(activeEndpoint.Url);
                throw new HttpRequestException("Network error");
            }
            url.ShouldBe(disabledEndpoint.Url);
            return Task.FromResult(expectedResult);
        };

        // Act
        var result = await _sut.ExecuteWithRetryAsync(chain, operation);

        // Assert
        result.ShouldBe(expectedResult);
        callCount.ShouldBe(2);
    }

    [Test]
    public async Task ExecuteWithRetryAsync_WhenOperationCancelled_ShouldPropagateOperationCanceledException()
    {
        // Arrange
        var chain = Chain.MainNet;

        var endpoint = new RpcEndpoint
        {
            Id = Guid.NewGuid(),
            Chain = chain,
            Url = "https://eth-rpc-mainnet.example.com",
            State = RpcState.Active,
            Priority = 1,
            ConsecutiveErrors = 0
        };

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { endpoint });

        using var cts = new CancellationTokenSource();

        Func<string, CancellationToken, Task<string>> operation = (url, ct) =>
        {
            // Cancel the token and then observe cancellation
            cts.Cancel();
            ct.ThrowIfCancellationRequested();
            return Task.FromResult("should-not-be-returned");
        };

        // Act & Assert
        await Should.ThrowAsync<OperationCanceledException>(
            async () => await _sut.ExecuteWithRetryAsync(chain, operation, cancellationToken: cts.Token));

        // Endpoint should not be marked as failed
        await _repository.DidNotReceive()
            .UpdateAsync(Arg.Any<RpcEndpoint>(), Arg.Any<CancellationToken>());
    }
    #endregion
}
