#pragma warning disable S1075 // URIs should not be hardcoded
#pragma warning disable S1192 // String literals should not be duplicated
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nethereum.Signer;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using NUnit.Framework;
using RpcProvider.Core.Configuration;
using RpcProvider.Core.Exceptions;
using RpcProvider.Core.Interfaces;
using RpcProvider.Core.Models;
using RpcProvider.Core.Services;
using Shouldly;
using System.Text;

namespace RpcProvider.Core.Tests.Services;

[TestFixture]
public class RpcUrlProviderTests
{
    private IRpcRepository _repository = null!;
    private IDistributedCache _cache = null!;
    private RpcProviderOptions _options = null!;
    private RpcUrlProvider _sut = null!;

    [SetUp]
    public void SetUp()
    {
        _repository = Substitute.For<IRpcRepository>();
        _cache = Substitute.For<IDistributedCache>();
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
        var cachedBytes = Encoding.UTF8.GetBytes(cachedUrl);

        _cache.GetAsync(cacheKey, Arg.Any<CancellationToken>())
            .Returns(cachedBytes);

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

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(endpoints);

        // Act
        var result = await _sut.GetBestRpcUrlAsync(chain);

        // Assert
        result.ShouldBe("https://eth-rpc-2.example.com"); // Priority 1, ConsecutiveErrors 0
        await _cache.Received(1).SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
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

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

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

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

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

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns((byte[]?)null);

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
        await _cache.Received(1).SetAsync(Arg.Any<string>(), Arg.Any<byte[]>(), Arg.Any<DistributedCacheEntryOptions>(), Arg.Any<CancellationToken>());
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
        Should.NotThrow(async () => await _sut.MarkAsFailedAsync(url, exception));
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

        _cache.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<byte[]?>(new Exception("Cache error")));

        _repository.GetByChainAndStateAsync(chain, RpcState.Active, Arg.Any<CancellationToken>())
            .Returns(new List<RpcEndpoint> { endpoint });

        // Act
        var result = await _sut.GetBestRpcUrlAsync(chain);

        // Assert
        result.ShouldBe("https://eth-rpc.example.com");
    }
}
