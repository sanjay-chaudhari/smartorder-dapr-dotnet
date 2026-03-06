using Dapr;
using Dapr.Client;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OrderService.Services;

namespace OrderService.Tests;

public class ConfigurationServiceTests
{
    private static ConfigurationService CreateService(DaprClient daprClient) =>
        new(daprClient, NullLogger<ConfigurationService>.Instance);

    private static DaprClient BuildConfigClient(
        int maxOrderQuantity = 50,
        bool discountEnabled = true)
    {
        var daprClient = Substitute.For<DaprClient>();
        var items = new Dictionary<string, ConfigurationItem>
        {
            ["max-order-quantity"] = new ConfigurationItem(maxOrderQuantity.ToString(), string.Empty, new Dictionary<string, string>()),
            ["discount-enabled"] = new ConfigurationItem(discountEnabled.ToString().ToLower(), string.Empty, new Dictionary<string, string>())
        };
        var response = new GetConfigurationResponse(items);
        daprClient
            .GetConfiguration(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(response));
        return daprClient;
    }

    // -----------------------------------------------------------------------
    // Config read on startup
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InitializeAsync_WhenConfigStoreAvailable_ShouldApplyMaxOrderQuantity()
    {
        var daprClient = BuildConfigClient(maxOrderQuantity: 42, discountEnabled: false);
        var sut = CreateService(daprClient);

        await sut.InitializeAsync(CancellationToken.None);

        sut.MaxOrderQuantity.Should().Be(42);
    }

    [Fact]
    public async Task InitializeAsync_WhenConfigStoreAvailable_ShouldApplyDiscountEnabled()
    {
        var daprClient = BuildConfigClient(maxOrderQuantity: 100, discountEnabled: true);
        var sut = CreateService(daprClient);

        await sut.InitializeAsync(CancellationToken.None);

        sut.DiscountEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task InitializeAsync_WhenConfigStoreAvailable_ShouldReadFromConfigstoreComponent()
    {
        var daprClient = BuildConfigClient();
        var sut = CreateService(daprClient);

        await sut.InitializeAsync(CancellationToken.None);

        await daprClient.Received(1).GetConfiguration(
            "configstore",
            Arg.Is<IReadOnlyList<string>>(keys =>
                keys.Contains("max-order-quantity") && keys.Contains("discount-enabled")),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Default fallback when ConfigStore is unavailable
    // -----------------------------------------------------------------------

    [Fact]
    public async Task InitializeAsync_WhenConfigStoreUnavailable_ShouldUseDefaultMaxOrderQuantity()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetConfiguration(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new DaprException("configstore unavailable"));

        var sut = CreateService(daprClient);

        await sut.InitializeAsync(CancellationToken.None);

        sut.MaxOrderQuantity.Should().Be(100);
    }

    [Fact]
    public async Task InitializeAsync_WhenConfigStoreUnavailable_ShouldUseDefaultDiscountDisabled()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetConfiguration(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new DaprException("configstore unavailable"));

        var sut = CreateService(daprClient);

        await sut.InitializeAsync(CancellationToken.None);

        sut.DiscountEnabled.Should().BeFalse();
    }

    [Fact]
    public async Task InitializeAsync_WhenConfigStoreUnavailable_ShouldNotThrow()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetConfiguration(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new DaprException("configstore unavailable"));

        var sut = CreateService(daprClient);

        var act = async () => await sut.InitializeAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // -----------------------------------------------------------------------
    // Default values before initialization
    // -----------------------------------------------------------------------

    [Fact]
    public void MaxOrderQuantity_BeforeInitialization_ShouldReturnDefault100()
    {
        var daprClient = Substitute.For<DaprClient>();
        var sut = CreateService(daprClient);

        sut.MaxOrderQuantity.Should().Be(100);
    }

    [Fact]
    public void DiscountEnabled_BeforeInitialization_ShouldReturnFalse()
    {
        var daprClient = Substitute.For<DaprClient>();
        var sut = CreateService(daprClient);

        sut.DiscountEnabled.Should().BeFalse();
    }
}
