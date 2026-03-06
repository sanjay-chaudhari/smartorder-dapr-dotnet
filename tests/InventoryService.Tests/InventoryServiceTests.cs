using Dapr;
using Dapr.Client;
using FluentAssertions;
using InventoryService.Models;
using InventoryService.Services;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace InventoryService.Tests;

public class InventoryServiceTests
{
    private static Services.InventoryService CreateService(DaprClient daprClient) =>
        new(daprClient, NullLogger<Services.InventoryService>.Instance);

    private static readonly StateOptions StrongConsistency = new()
    {
        Consistency = ConsistencyMode.Strong
    };

    // -----------------------------------------------------------------------
    // ReserveAsync — successful reservation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReserveAsync_WhenSufficientStock_ShouldReturnSuccess()
    {
        var item = new InventoryItem("prod-1", AvailableQuantity: 10, ReservedQuantity: 0);
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((item, "etag-1"));
        daprClient
            .TrySaveStateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InventoryItem>(),
                Arg.Any<string>(), Arg.Any<StateOptions>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = CreateService(daprClient);
        var result = await sut.ReserveAsync(
            new ReserveInventoryRequest("prod-1", 3, "order-abc"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ReserveAsync_WhenSufficientStock_ShouldDecrementAvailableAndIncrementReserved()
    {
        var item = new InventoryItem("prod-1", AvailableQuantity: 10, ReservedQuantity: 2);
        InventoryItem? saved = null;

        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((item, "etag-1"));
        daprClient
            .TrySaveStateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InventoryItem>(),
                Arg.Any<string>(), Arg.Any<StateOptions>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                saved = callInfo.ArgAt<InventoryItem>(2);
                return Task.FromResult(true);
            });

        var sut = CreateService(daprClient);
        await sut.ReserveAsync(new ReserveInventoryRequest("prod-1", 3, "order-abc"), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.AvailableQuantity.Should().Be(7);
        saved.ReservedQuantity.Should().Be(5);
    }

    [Fact]
    public async Task ReserveAsync_WhenSufficientStock_ShouldUseCorrectStateKeyAndComponent()
    {
        var item = new InventoryItem("prod-1", AvailableQuantity: 10, ReservedQuantity: 0);
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((item, "etag-1"));
        daprClient
            .TrySaveStateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InventoryItem>(),
                Arg.Any<string>(), Arg.Any<StateOptions>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = CreateService(daprClient);
        await sut.ReserveAsync(new ReserveInventoryRequest("prod-1", 3, "order-abc"), CancellationToken.None);

        await daprClient.Received(1).GetStateAndETagAsync<InventoryItem>(
            "statestore", "inventory-prod-1",
            Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());

        await daprClient.Received(1).TrySaveStateAsync(
            "statestore", "inventory-prod-1", Arg.Any<InventoryItem>(),
            "etag-1", Arg.Any<StateOptions>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // ReserveAsync — insufficient stock
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReserveAsync_WhenInsufficientStock_ShouldReturnFailure()
    {
        var item = new InventoryItem("prod-1", AvailableQuantity: 2, ReservedQuantity: 0);
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((item, "etag-1"));

        var sut = CreateService(daprClient);
        var result = await sut.ReserveAsync(
            new ReserveInventoryRequest("prod-1", 5, "order-abc"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task ReserveAsync_WhenInsufficientStock_ShouldNotCallTrySaveState()
    {
        var item = new InventoryItem("prod-1", AvailableQuantity: 2, ReservedQuantity: 0);
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((item, "etag-1"));

        var sut = CreateService(daprClient);
        await sut.ReserveAsync(new ReserveInventoryRequest("prod-1", 5, "order-abc"), CancellationToken.None);

        await daprClient.DidNotReceive().TrySaveStateAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InventoryItem>(),
            Arg.Any<string>(), Arg.Any<StateOptions>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReserveAsync_WhenProductNotFound_ShouldReturnFailure()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(((InventoryItem?)null, (string)"etag-1"));

        var sut = CreateService(daprClient);
        var result = await sut.ReserveAsync(
            new ReserveInventoryRequest("prod-missing", 1, "order-abc"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not found");
    }

    // -----------------------------------------------------------------------
    // ReserveAsync — DaprException handling
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReserveAsync_WhenDaprExceptionThrown_ShouldRethrow()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new DaprException("statestore unavailable"));

        var sut = CreateService(daprClient);
        var act = async () => await sut.ReserveAsync(
            new ReserveInventoryRequest("prod-1", 1, "order-abc"), CancellationToken.None);

        await act.Should().ThrowAsync<DaprException>();
    }

    // -----------------------------------------------------------------------
    // ReleaseAsync — successful release
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReleaseAsync_WhenItemExists_ShouldReturnSuccess()
    {
        var item = new InventoryItem("prod-1", AvailableQuantity: 5, ReservedQuantity: 3);
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((item, "etag-1"));
        daprClient
            .TrySaveStateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InventoryItem>(),
                Arg.Any<string>(), Arg.Any<StateOptions>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = CreateService(daprClient);
        var result = await sut.ReleaseAsync(
            new ReleaseInventoryRequest("prod-1", 3, "order-abc"), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseAsync_WhenItemExists_ShouldIncrementAvailableAndDecrementReserved()
    {
        var item = new InventoryItem("prod-1", AvailableQuantity: 5, ReservedQuantity: 3);
        InventoryItem? saved = null;

        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((item, "etag-1"));
        daprClient
            .TrySaveStateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InventoryItem>(),
                Arg.Any<string>(), Arg.Any<StateOptions>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                saved = callInfo.ArgAt<InventoryItem>(2);
                return Task.FromResult(true);
            });

        var sut = CreateService(daprClient);
        await sut.ReleaseAsync(new ReleaseInventoryRequest("prod-1", 3, "order-abc"), CancellationToken.None);

        saved.Should().NotBeNull();
        saved!.AvailableQuantity.Should().Be(8);
        saved.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public async Task ReleaseAsync_WhenProductNotFound_ShouldReturnFailure()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(((InventoryItem?)null, (string)"etag-1"));

        var sut = CreateService(daprClient);
        var result = await sut.ReleaseAsync(
            new ReleaseInventoryRequest("prod-missing", 1, "order-abc"), CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // ReleaseAsync — DaprException handling
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReleaseAsync_WhenDaprExceptionThrown_ShouldRethrow()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new DaprException("statestore unavailable"));

        var sut = CreateService(daprClient);
        var act = async () => await sut.ReleaseAsync(
            new ReleaseInventoryRequest("prod-1", 1, "order-abc"), CancellationToken.None);

        await act.Should().ThrowAsync<DaprException>();
    }

    // -----------------------------------------------------------------------
    // Strong consistency — StateOptions verification
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReserveAsync_WhenSaving_ShouldUseStrongConsistency()
    {
        var item = new InventoryItem("prod-1", AvailableQuantity: 10, ReservedQuantity: 0);
        StateOptions? capturedOptions = null;

        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<InventoryItem>(
                Arg.Any<string>(), Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(), Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((item, "etag-1"));
        daprClient
            .TrySaveStateAsync(
                Arg.Any<string>(), Arg.Any<string>(), Arg.Any<InventoryItem>(),
                Arg.Any<string>(), Arg.Any<StateOptions>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedOptions = callInfo.ArgAt<StateOptions>(4);
                return Task.FromResult(true);
            });

        var sut = CreateService(daprClient);
        await sut.ReserveAsync(new ReserveInventoryRequest("prod-1", 1, "order-abc"), CancellationToken.None);

        capturedOptions.Should().NotBeNull();
        capturedOptions!.Consistency.Should().Be(ConsistencyMode.Strong);
    }
}
