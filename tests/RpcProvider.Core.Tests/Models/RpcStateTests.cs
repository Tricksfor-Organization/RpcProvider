using NUnit.Framework;
using RpcProvider.Core.Models;
using Shouldly;

namespace RpcProvider.Core.Tests.Models;

[TestFixture]
public class RpcStateTests
{
    [Test]
    public void RpcState_ShouldHaveCorrectValues()
    {
        // Assert
        ((int)RpcState.Active).ShouldBe(0);
        ((int)RpcState.Error).ShouldBe(1);
        ((int)RpcState.Disabled).ShouldBe(2);
    }

    [Test]
    public void RpcState_ShouldBeComparable()
    {
        // Arrange
        var active = RpcState.Active;
        var error = RpcState.Error;
        var disabled = RpcState.Disabled;

        // Assert
        active.ShouldBe(RpcState.Active);
        error.ShouldBe(RpcState.Error);
        disabled.ShouldBe(RpcState.Disabled);
        active.ShouldNotBe(error);
        error.ShouldNotBe(disabled);
    }

    [Test]
    public void RpcState_ShouldConvertToString()
    {
        // Arrange
        var active = RpcState.Active;
        var error = RpcState.Error;
        var disabled = RpcState.Disabled;

        // Assert
        active.ToString().ShouldBe("Active");
        error.ToString().ShouldBe("Error");
        disabled.ToString().ShouldBe("Disabled");
    }
}
