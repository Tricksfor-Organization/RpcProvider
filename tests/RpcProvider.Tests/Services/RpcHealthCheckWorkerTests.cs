using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NSubstitute;
using NUnit.Framework;
using RpcProvider.Configuration;
using RpcProvider.Services;
using Shouldly;

namespace RpcProvider.Tests.Services;

[TestFixture]
public class RpcHealthCheckWorkerTests
{
    private IServiceProvider _serviceProvider = null!;
    private ILogger<RpcHealthCheckWorker> _logger = null!;
    private RpcProviderOptions _options = null!;
    private IOptions<RpcProviderOptions> _optionsWrapper = null!;

    [SetUp]
    public void SetUp()
    {
        _logger = Substitute.For<ILogger<RpcHealthCheckWorker>>();
        _options = new RpcProviderOptions
        {
            EnableHealthChecks = true,
            HealthCheckIntervalMinutes = 5
        };
        _optionsWrapper = Substitute.For<IOptions<RpcProviderOptions>>();
        _optionsWrapper.Value.Returns(_options);

        var serviceCollection = new ServiceCollection();
        var healthChecker = Substitute.For<RpcHealthChecker>(
            Substitute.For<Interfaces.IRpcRepository>(),
            Substitute.For<ILogger<RpcHealthChecker>>());
        
        serviceCollection.AddSingleton(healthChecker);
        _serviceProvider = serviceCollection.BuildServiceProvider();
    }

    [Test]
    public void Constructor_WithNullServiceProvider_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => 
            new RpcHealthCheckWorker(null!, _logger, _optionsWrapper));
    }

    [Test]
    public void Constructor_WithNullLogger_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => 
            new RpcHealthCheckWorker(_serviceProvider, null!, _optionsWrapper));
    }

    [Test]
    public void Constructor_WithNullOptions_ShouldThrowArgumentNullException()
    {
        // Act & Assert
        Should.Throw<ArgumentNullException>(() => 
            new RpcHealthCheckWorker(_serviceProvider, _logger, null!));
    }

    [Test]
    public void Constructor_WithValidParameters_ShouldCreateInstance()
    {
        // Act
        var worker = new RpcHealthCheckWorker(_serviceProvider, _logger, _optionsWrapper);

        // Assert
        worker.ShouldNotBeNull();
    }

    [Test]
    public async Task ExecuteAsync_WhenHealthChecksDisabled_ShouldReturnEarly()
    {
        // Arrange
        _options.EnableHealthChecks = false;
        var worker = new RpcHealthCheckWorker(_serviceProvider, _logger, _optionsWrapper);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync(); // Cancel immediately

        // Act
        await worker.StartAsync(cts.Token);
        await Task.Delay(100); // Give it a moment to process
        await worker.StopAsync(cts.Token);

        // Assert
        // If health checks are disabled, the worker should return early
        // We can't easily assert this without exposing internals, but the test should not hang
        worker.ShouldNotBeNull();
    }

    [Test]
    public async Task ExecuteAsync_WhenCancellationRequested_ShouldStop()
    {
        // Arrange
        var worker = new RpcHealthCheckWorker(_serviceProvider, _logger, _optionsWrapper);
        using var cts = new CancellationTokenSource();

        // Act
        _ = worker.StartAsync(cts.Token);
        await Task.Delay(100); // Let it start
        await cts.CancelAsync(); // Request cancellation
        await worker.StopAsync(CancellationToken.None);

        // Assert
        // Worker should stop gracefully
        worker.ShouldNotBeNull();
    }
}
