#pragma warning disable S1075 // URIs should not be hardcoded
using Nethereum.Signer;
using RpcProvider.Exceptions;

namespace RpcProvider.Tests.Exceptions;

[TestFixture]
public class AllRpcEndpointsFailedExceptionTests
{
    [Test]
    public void Constructor_WithChainAndFailures_ShouldSetProperties()
    {
        // Arrange
        var chain = Chain.MainNet;
        var failures = new List<RpcEndpointFailure>
        {
            new("https://eth-rpc-1.example.com", new HttpRequestException("Network error 1")),
            new("https://eth-rpc-2.example.com", new HttpRequestException("Network error 2"))
        };

        // Act
        var exception = new AllRpcEndpointsFailedException(chain, failures);

        // Assert
        exception.Chain.ShouldBe(chain);
        exception.Failures.ShouldBe(failures);
        exception.Failures.Count.ShouldBe(2);
        exception.Message.ShouldBe($"All 2 RPC endpoint(s) failed for chain: {chain} ({(int)chain})");
    }

    [Test]
    public void Constructor_WithChainFailuresAndInnerException_ShouldSetAllProperties()
    {
        // Arrange
        var chain = Chain.Polygon;
        var failures = new List<RpcEndpointFailure>
        {
            new("https://polygon-rpc-1.example.com", new TimeoutException("Timeout"))
        };
        var innerException = new InvalidOperationException("Inner exception");

        // Act
        var exception = new AllRpcEndpointsFailedException(chain, failures, innerException);

        // Assert
        exception.Chain.ShouldBe(chain);
        exception.Failures.ShouldBe(failures);
        exception.InnerException.ShouldBe(innerException);
    }

    [Test]
    public void Constructor_WithNullFailures_ShouldThrowArgumentNullException()
    {
        // Arrange
        var chain = Chain.MainNet;

        // Act & Assert
        Should.Throw<ArgumentNullException>(() => new AllRpcEndpointsFailedException(chain, null!));
    }

    [Test]
    public void Exception_ShouldBeInstanceOfRpcProviderException()
    {
        // Arrange
        var chain = Chain.MainNet;
        var failures = new List<RpcEndpointFailure>
        {
            new("https://eth-rpc-1.example.com", new Exception("Test"))
        };

        // Act
        var exception = new AllRpcEndpointsFailedException(chain, failures);

        // Assert
        exception.ShouldBeOfType<AllRpcEndpointsFailedException>();
        exception.ShouldBeAssignableTo<RpcProviderException>();
    }

    [Test]
    public void RpcEndpointFailure_ShouldStoreUrlAndException()
    {
        // Arrange
        var url = "https://eth-rpc.example.com";
        var ex = new HttpRequestException("Network error");

        // Act
        var failure = new RpcEndpointFailure(url, ex);

        // Assert
        failure.Url.ShouldBe(url);
        failure.Exception.ShouldBe(ex);
    }

    [Test]
    public void RpcEndpointFailure_WithRecordEquality_ShouldBeEqual()
    {
        // Arrange
        var url = "https://eth-rpc.example.com";
        var ex = new HttpRequestException("Network error");

        var failure1 = new RpcEndpointFailure(url, ex);
        var failure2 = new RpcEndpointFailure(url, ex);

        // Assert
        failure1.ShouldBe(failure2);
    }

    [Test]
    public void Message_WithDifferentFailureCounts_ShouldReflectCount()
    {
        // Arrange & Act
        var exception1 = new AllRpcEndpointsFailedException(
            Chain.MainNet,
            new List<RpcEndpointFailure>
            {
                new("https://url1.example.com", new Exception())
            });

        var exception2 = new AllRpcEndpointsFailedException(
            Chain.Polygon,
            new List<RpcEndpointFailure>
            {
                new("https://url1.example.com", new Exception()),
                new("https://url2.example.com", new Exception()),
                new("https://url3.example.com", new Exception())
            });

        // Assert
        exception1.Message.ShouldContain("All 1 RPC endpoint(s) failed");
        exception2.Message.ShouldContain("All 3 RPC endpoint(s) failed");
    }
}
