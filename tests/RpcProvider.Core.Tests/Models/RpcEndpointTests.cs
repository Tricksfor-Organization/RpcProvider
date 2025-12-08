#pragma warning disable S1075 // URIs should not be hardcoded
using Nethereum.Signer;
using NUnit.Framework;
using RpcProvider.Core.Models;
using Shouldly;

namespace RpcProvider.Core.Tests.Models;

[TestFixture]
public class RpcEndpointTests
{
    [Test]
    public void RpcEndpoint_DefaultConstructor_ShouldInitializeProperties()
    {
        // Act
        var endpoint = new RpcEndpoint();

        // Assert
        endpoint.Id.ShouldBe(Guid.Empty);
        endpoint.Chain.ShouldBe(default(Chain));
        endpoint.Url.ShouldBe(string.Empty);
        endpoint.State.ShouldBe(default(RpcState));
        endpoint.Priority.ShouldBe(0);
        endpoint.ConsecutiveErrors.ShouldBe(0);
        endpoint.ErrorMessage.ShouldBeNull();
        endpoint.LastErrorAt.ShouldBeNull();
    }

    [Test]
    public void RpcEndpoint_SetProperties_ShouldStoreValues()
    {
        // Arrange
        var id = Guid.NewGuid();
        var chain = Chain.MainNet;
        var url = "https://eth-rpc.example.com";
        var state = RpcState.Active;
        var priority = 1;
        var consecutiveErrors = 2;
        var errorMessage = "Test error";
        var lastErrorAt = DateTime.UtcNow;
        var created = DateTime.UtcNow.AddDays(-1);
        var modified = DateTime.UtcNow;

        // Act
        var endpoint = new RpcEndpoint
        {
            Id = id,
            Chain = chain,
            Url = url,
            State = state,
            Priority = priority,
            ConsecutiveErrors = consecutiveErrors,
            ErrorMessage = errorMessage,
            LastErrorAt = lastErrorAt,
            Created = created,
            Modified = modified
        };

        // Assert
        endpoint.Id.ShouldBe(id);
        endpoint.Chain.ShouldBe(chain);
        endpoint.Url.ShouldBe(url);
        endpoint.State.ShouldBe(state);
        endpoint.Priority.ShouldBe(priority);
        endpoint.ConsecutiveErrors.ShouldBe(consecutiveErrors);
        endpoint.ErrorMessage.ShouldBe(errorMessage);
        endpoint.LastErrorAt.ShouldBe(lastErrorAt);
        endpoint.Created.ShouldBe(created);
        endpoint.Modified.ShouldBe(modified);
    }

    [Test]
    public void RpcEndpoint_DifferentChains_ShouldStoreDifferentValues()
    {
        // Arrange & Act
        var ethereumEndpoint = new RpcEndpoint { Chain = Chain.MainNet };
        var polygonEndpoint = new RpcEndpoint { Chain = Chain.Polygon };
        var bscEndpoint = new RpcEndpoint { Chain = Chain.Sepolia };

        // Assert
        ethereumEndpoint.Chain.ShouldBe(Chain.MainNet);
        polygonEndpoint.Chain.ShouldBe(Chain.Polygon);
        bscEndpoint.Chain.ShouldBe(Chain.Sepolia);
        ethereumEndpoint.Chain.ShouldNotBe(polygonEndpoint.Chain);
    }

    [Test]
    public void RpcEndpoint_DifferentStates_ShouldStoreDifferentValues()
    {
        // Arrange & Act
        var activeEndpoint = new RpcEndpoint { State = RpcState.Active };
        var errorEndpoint = new RpcEndpoint { State = RpcState.Error };
        var disabledEndpoint = new RpcEndpoint { State = RpcState.Disabled };

        // Assert
        activeEndpoint.State.ShouldBe(RpcState.Active);
        errorEndpoint.State.ShouldBe(RpcState.Error);
        disabledEndpoint.State.ShouldBe(RpcState.Disabled);
    }
}
