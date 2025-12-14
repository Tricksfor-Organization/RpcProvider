using NUnit.Framework;
using RpcProvider.Configuration;
using Shouldly;

namespace RpcProvider.Tests.Configuration;

[TestFixture]
public class RpcProviderOptionsTests
{
    [Test]
    public void RpcProviderOptions_DefaultConstructor_ShouldSetDefaultValues()
    {
        // Act
        var options = new RpcProviderOptions();

        // Assert
        options.CacheDurationSeconds.ShouldBe(300);
        options.MaxConsecutiveErrorsBeforeDisable.ShouldBe(5);
        options.RequestTimeoutSeconds.ShouldBe(30);
        options.AllowDisabledEndpointsAsFallback.ShouldBeFalse();
        options.HealthCheckIntervalMinutes.ShouldBe(5);
        options.EnableHealthChecks.ShouldBeTrue();
        options.BaseBackoffMinutes.ShouldBe(1);
        options.MaxBackoffMinutes.ShouldBe(30);
    }

    [Test]
    public void RpcProviderOptions_SetCustomValues_ShouldStoreValues()
    {
        // Arrange & Act
        var options = new RpcProviderOptions
        {
            CacheDurationSeconds = 600,
            MaxConsecutiveErrorsBeforeDisable = 10,
            RequestTimeoutSeconds = 60,
            AllowDisabledEndpointsAsFallback = true,
            HealthCheckIntervalMinutes = 10,
            EnableHealthChecks = false,
            BaseBackoffMinutes = 2,
            MaxBackoffMinutes = 120
        };

        // Assert
        options.CacheDurationSeconds.ShouldBe(600);
        options.MaxConsecutiveErrorsBeforeDisable.ShouldBe(10);
        options.RequestTimeoutSeconds.ShouldBe(60);
        options.AllowDisabledEndpointsAsFallback.ShouldBeTrue();
        options.HealthCheckIntervalMinutes.ShouldBe(10);
        options.EnableHealthChecks.ShouldBeFalse();
        options.BaseBackoffMinutes.ShouldBe(2);
        options.MaxBackoffMinutes.ShouldBe(120);
    }

    [Test]
    public void RpcProviderOptions_SetZeroValues_ShouldAllowZero()
    {
        // Arrange & Act
        var options = new RpcProviderOptions
        {
            CacheDurationSeconds = 0,
            MaxConsecutiveErrorsBeforeDisable = 0,
            RequestTimeoutSeconds = 0,
            HealthCheckIntervalMinutes = 0,
            BaseBackoffMinutes = 0,
            MaxBackoffMinutes = 0
        };

        // Assert
        options.CacheDurationSeconds.ShouldBe(0);
        options.MaxConsecutiveErrorsBeforeDisable.ShouldBe(0);
        options.RequestTimeoutSeconds.ShouldBe(0);
        options.HealthCheckIntervalMinutes.ShouldBe(0);
        options.BaseBackoffMinutes.ShouldBe(0);
        options.MaxBackoffMinutes.ShouldBe(0);
    }

    [Test]
    public void RpcProviderOptions_ToggleBooleanFlags_ShouldWorkCorrectly()
    {
        // Arrange
        var options = new RpcProviderOptions();

        // Act & Assert - Toggle AllowDisabledEndpointsAsFallback
        options.AllowDisabledEndpointsAsFallback.ShouldBeFalse();
        options.AllowDisabledEndpointsAsFallback = true;
        options.AllowDisabledEndpointsAsFallback.ShouldBeTrue();

        // Act & Assert - Toggle EnableHealthChecks
        options.EnableHealthChecks.ShouldBeTrue();
        options.EnableHealthChecks = false;
        options.EnableHealthChecks.ShouldBeFalse();
    }
}
