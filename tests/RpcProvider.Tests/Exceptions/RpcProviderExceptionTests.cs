using NUnit.Framework;
using RpcProvider.Exceptions;
using Shouldly;

namespace RpcProvider.Tests.Exceptions;

[TestFixture]
public class RpcProviderExceptionTests
{
    [Test]
    public void Constructor_WithMessage_ShouldSetMessage()
    {
        // Arrange
        var message = "Test error message";

        // Act
        var exception = new RpcProviderException(message);

        // Assert
        exception.Message.ShouldBe(message);
    }

    [Test]
    public void Constructor_WithMessageAndInnerException_ShouldSetBothProperties()
    {
        // Arrange
        var message = "Test error message";
        var innerException = new InvalidOperationException("Inner exception");

        // Act
        var exception = new RpcProviderException(message, innerException);

        // Assert
        exception.Message.ShouldBe(message);
        exception.InnerException.ShouldBe(innerException);
    }

    [Test]
    public void Exception_ShouldBeInstanceOfException()
    {
        // Arrange & Act
        var exception = new RpcProviderException("Test");

        // Assert
        exception.ShouldBeOfType<RpcProviderException>();
        exception.ShouldBeAssignableTo<Exception>();
    }
}
