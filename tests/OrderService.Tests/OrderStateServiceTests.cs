using Dapr.Client;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using OrderService.Models;
using OrderService.Services;

namespace OrderService.Tests;

// ---------------------------------------------------------------------------
// Custom arbitraries for FsCheck
// ---------------------------------------------------------------------------
public static class SmartOrderArbitraries
{
    public static Arbitrary<Order> Order()
    {
        var gen =
            from orderId in Gen.Fresh(() => Guid.NewGuid().ToString("N"))
            from productId in Gen.Fresh(() => Guid.NewGuid().ToString("N"))
            from quantity in Gen.Choose(1, 1000)
            from priceCents in Gen.Choose(1, 100_000)
            from status in Gen.Elements(
                OrderStatus.Pending,
                OrderStatus.InventoryReserved,
                OrderStatus.PaymentProcessed,
                OrderStatus.Completed,
                OrderStatus.InventoryFailed,
                OrderStatus.PaymentFailed,
                OrderStatus.Failed)
            select new Order(
                orderId,
                productId,
                quantity,
                priceCents / 100m,
                status,
                DateTimeOffset.UtcNow,
                null);

        return gen.ToArbitrary();
    }

    public static Arbitrary<OrderStatus> AnyOrderStatus() =>
        Gen.Elements(
            Models.OrderStatus.Pending,
            Models.OrderStatus.InventoryReserved,
            Models.OrderStatus.PaymentProcessed,
            Models.OrderStatus.Completed,
            Models.OrderStatus.InventoryFailed,
            Models.OrderStatus.PaymentFailed,
            Models.OrderStatus.Failed)
        .ToArbitrary();
}

// ---------------------------------------------------------------------------
// OrderStateServiceTests
// ---------------------------------------------------------------------------
public class OrderStateServiceTests
{
    private static OrderStateService CreateService(DaprClient daprClient) =>
        new(daprClient, NullLogger<OrderStateService>.Instance);

    // -----------------------------------------------------------------------
    // Property 8: State key pattern is always order-{orderId}
    // Feature: smart-order, Property 8: State key pattern is always order-{orderId}
    // Validates: Requirements 4.1, 4.6
    // -----------------------------------------------------------------------

    [Property(Arbitrary = new[] { typeof(SmartOrderArbitraries) }, MaxTest = 100)]
    public async Task SaveOrderAsync_WhenCalled_UsesCorrectStateKeyAndComponent(Order order)
    {
        // For any orderId, SaveOrderAsync must call ExecuteStateTransactionAsync
        // with key "order-{orderId}" on component "statestore"
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .ExecuteStateTransactionAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<StateTransactionRequest>>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var sut = CreateService(daprClient);
        await sut.SaveOrderAsync(order, CancellationToken.None);

        var expectedKey = $"order-{order.OrderId}";

        await daprClient.Received(1).ExecuteStateTransactionAsync(
            "statestore",
            Arg.Is<IReadOnlyList<StateTransactionRequest>>(ops =>
                ops.Count == 1 && ops[0].Key == expectedKey),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());
    }

    [Property(Arbitrary = new[] { typeof(SmartOrderArbitraries) }, MaxTest = 100)]
    public async Task GetOrderAsync_WhenCalled_UsesCorrectStateKeyAndComponent(Order order)
    {
        // For any orderId, GetOrderAsync must call GetStateAndETagAsync
        // with key "order-{orderId}" on component "statestore"
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<Order>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((order, "etag-1"));

        var sut = CreateService(daprClient);
        await sut.GetOrderAsync(order.OrderId, CancellationToken.None);

        var expectedKey = $"order-{order.OrderId}";

        await daprClient.Received(1).GetStateAndETagAsync<Order>(
            "statestore",
            expectedKey,
            Arg.Any<ConsistencyMode?>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());
    }

    [Property(Arbitrary = new[] { typeof(SmartOrderArbitraries) }, MaxTest = 100)]
    public async Task UpdateOrderStatusAsync_WhenCalled_UsesCorrectStateKeyAndComponent(Order order, OrderStatus newStatus)
    {
        // For any orderId, UpdateOrderStatusAsync must use key "order-{orderId}" on "statestore"
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetStateAndETagAsync<Order>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((order, "etag-1"));

        daprClient
            .TrySaveStateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Order>(),
                Arg.Any<string>(),
                Arg.Any<StateOptions>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = CreateService(daprClient);
        await sut.UpdateOrderStatusAsync(order.OrderId, newStatus, CancellationToken.None);

        var expectedKey = $"order-{order.OrderId}";

        await daprClient.Received(1).TrySaveStateAsync(
            "statestore",
            expectedKey,
            Arg.Any<Order>(),
            Arg.Any<string>(),
            Arg.Any<StateOptions>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Property 9: State round-trip preserves order data
    // Feature: smart-order, Property 9: State round-trip preserves order data
    // Validates: Requirements 4.2, 14.5
    // -----------------------------------------------------------------------

    [Property(Arbitrary = new[] { typeof(SmartOrderArbitraries) }, MaxTest = 100)]
    public async Task SaveAndGetOrderAsync_WhenRoundTripped_PreservesAllFields(Order order)
    {
        // For any Order, saving it and reading it back returns equivalent field values
        var daprClient = Substitute.For<DaprClient>();
        Order? captured = null;

        daprClient
            .ExecuteStateTransactionAsync(
                Arg.Any<string>(),
                Arg.Any<IReadOnlyList<StateTransactionRequest>>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var ops = callInfo.Arg<IReadOnlyList<StateTransactionRequest>>();
                captured = System.Text.Json.JsonSerializer.Deserialize<Order>(ops[0].Value);
                return Task.CompletedTask;
            });

        daprClient
            .GetStateAndETagAsync<Order>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
#pragma warning disable CS8619
            .Returns(callInfo => Task.FromResult<(Order?, string)>((captured, "etag-1")));
#pragma warning restore CS8619

        var sut = CreateService(daprClient);
        await sut.SaveOrderAsync(order, CancellationToken.None);
        var retrieved = await sut.GetOrderAsync(order.OrderId, CancellationToken.None);

        retrieved.Should().NotBeNull();
        retrieved!.OrderId.Should().Be(order.OrderId);
        retrieved.ProductId.Should().Be(order.ProductId);
        retrieved.Quantity.Should().Be(order.Quantity);
        retrieved.Price.Should().Be(order.Price);
        retrieved.Status.Should().Be(order.Status);
    }

    // -----------------------------------------------------------------------
    // Property 10: ETag conflict retries exactly 3 times before failing
    // Feature: smart-order, Property 10: ETag conflict retries exactly 3 times before failing
    // Validates: Requirements 4.4
    // -----------------------------------------------------------------------

    [Property(Arbitrary = new[] { typeof(SmartOrderArbitraries) }, MaxTest = 100)]
    public async Task UpdateOrderStatusAsync_WhenETagAlwaysFails_RetriesExactly3TimesThenThrows(Order order, OrderStatus newStatus)
    {
        // When TrySaveStateAsync always returns false, the service must attempt
        // the read-modify-write cycle exactly 3 times then throw ConcurrencyException
        var daprClient = Substitute.For<DaprClient>();

        daprClient
            .GetStateAndETagAsync<Order>(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ConsistencyMode?>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns((order, "etag-stale"));

        daprClient
            .TrySaveStateAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<Order>(),
                Arg.Any<string>(),
                Arg.Any<StateOptions>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(false);

        var sut = CreateService(daprClient);

        var act = async () => await sut.UpdateOrderStatusAsync(order.OrderId, newStatus, CancellationToken.None);

        await act.Should().ThrowAsync<ConcurrencyException>()
            .WithMessage($"*{order.OrderId}*");

        // GetStateAndETagAsync must be called exactly 3 times (one per attempt)
        await daprClient.Received(3).GetStateAndETagAsync<Order>(
            "statestore",
            $"order-{order.OrderId}",
            Arg.Any<ConsistencyMode?>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());

        // TrySaveStateAsync must be called exactly 3 times (one per attempt)
        await daprClient.Received(3).TrySaveStateAsync(
            "statestore",
            $"order-{order.OrderId}",
            Arg.Any<Order>(),
            Arg.Any<string>(),
            Arg.Any<StateOptions>(),
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());
    }
}
