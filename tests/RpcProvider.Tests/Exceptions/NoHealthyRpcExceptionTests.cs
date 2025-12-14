using Nethereum.Signer;
using NUnit.Framework;
using RpcProvider.Exceptions;
using Shouldly;

namespace RpcProvider.Tests.Exceptions;

[TestFixture]
public class NoHealthyRpcExceptionTests
{
    [Test]
    public void Constructor_WithChainOnly_ShouldSetMessageCorrectly()
    {
        // Arrange
        var chain = Chain.MainNet;

        // Act
        var exception = new NoHealthyRpcException(chain);

        // Assert
        exception.Message.ShouldBe($"No healthy RPC endpoints available for chain: {chain} ({(int)chain})");
        exception.Chain.ShouldBe(chain);
    }

    [Test]
    public void Constructor_WithChainAndMessage_ShouldSetMessageCorrectly()
    {
        // Arrange
        var chain = Chain.Polygon;
        var customMessage = "Custom error message";

        // Act
        var exception = new NoHealthyRpcException(chain, customMessage);

        // Assert
        exception.Message.ShouldBe(customMessage);
        exception.Chain.ShouldBe(chain);
    }

    [Test]
    public void Constructor_WithChainMessageAndInnerException_ShouldSetAllProperties()
    {
        // Arrange
        var chain = Chain.Sepolia;
        var customMessage = "Custom error with inner exception";
        var innerException = new InvalidOperationException("Inner exception");

        // Act
        var exception = new NoHealthyRpcException(chain, customMessage, innerException);

        // Assert
        exception.Message.ShouldBe(customMessage);
        exception.Chain.ShouldBe(chain);
        exception.InnerException.ShouldBe(innerException);
    }

    [Test]
    public void Exception_ShouldBeInstanceOfRpcProviderException()
    {
        // Arrange & Act
        var exception = new NoHealthyRpcException(Chain.MainNet);

        // Assert
        exception.ShouldBeOfType<NoHealthyRpcException>();
        exception.ShouldBeAssignableTo<RpcProviderException>();
    }
}
