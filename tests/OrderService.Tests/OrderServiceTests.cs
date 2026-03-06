// Feature: smart-order, Property 1: Valid order creation returns 202 with Pending status
// Validates: Requirements 1.2, 1.3, 1.4
// Feature: smart-order, Property 2: Invalid order inputs are rejected with 400
// Validates: Requirements 1.2, 1.5

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
// Arbitraries for valid CreateOrderRequest inputs
// ---------------------------------------------------------------------------
public static class OrderServiceArbitraries
{
    /// <summary>
    /// Generates valid CreateOrderRequest values: non-empty ProductId, Quantity > 0, Price > 0,
    /// Quantity within the default max-order-quantity of 100.
    /// </summary>
    public static Arbitrary<CreateOrderRequest> ValidCreateOrderRequest()
    {
        var gen =
            from productId in Gen.Fresh(() => Guid.NewGuid().ToString("N"))
            from quantity in Gen.Choose(1, 100)
            from priceCents in Gen.Choose(1, 100_000)
            select new CreateOrderRequest(productId, quantity, priceCents / 100m);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates requests with Quantity &lt;= 0 (zero or negative).
    /// </summary>
    public static Arbitrary<CreateOrderRequest> ZeroOrNegativeQuantityRequest()
    {
        var gen =
            from productId in Gen.Fresh(() => Guid.NewGuid().ToString("N"))
            from quantity in Gen.Choose(int.MinValue / 2, 0)
            from priceCents in Gen.Choose(1, 100_000)
            select new CreateOrderRequest(productId, quantity, priceCents / 100m);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates requests with Price &lt;= 0 (zero or negative).
    /// </summary>
    public static Arbitrary<CreateOrderRequest> ZeroOrNegativePriceRequest()
    {
        var gen =
            from productId in Gen.Fresh(() => Guid.NewGuid().ToString("N"))
            from quantity in Gen.Choose(1, 100)
            from priceNegCents in Gen.Choose(0, 100_000)
            select new CreateOrderRequest(productId, quantity, -(priceNegCents / 100m));

        return gen.ToArbitrary();
    }
}

// ---------------------------------------------------------------------------
// OrderServiceTests
// ---------------------------------------------------------------------------
public class OrderServiceTests
{
    // Build a fully-wired OrderService with controllable collaborators.
    // DaprClient is only needed for PublishEventAsync — it's abstract so NSubstitute can substitute it.
    // IInventoryClient is our own interface, trivially mockable.
    private static Services.OrderService CreateService(
        DaprClient? daprClient = null,
        IInventoryClient? inventoryClient = null,
        IOrderStateService? stateService = null,
        IConfigurationService? configService = null)
    {
        daprClient ??= BuildPublishSucceedsDaprClient();
        inventoryClient ??= BuildSuccessInventoryClient();
        stateService ??= Substitute.For<IOrderStateService>();
        configService ??= BuildDefaultConfigService();

        return new Services.OrderService(
            daprClient,
            inventoryClient,
            stateService,
            configService,
            NullLogger<Services.OrderService>.Instance,
            new OrderValidator());
    }

    private static IConfigurationService BuildDefaultConfigService(
        int maxQuantity = 100,
        bool discountEnabled = false)
    {
        var cfg = Substitute.For<IConfigurationService>();
        cfg.MaxOrderQuantity.Returns(maxQuantity);
        cfg.DiscountEnabled.Returns(discountEnabled);
        return cfg;
    }

    /// <summary>
    /// DaprClient substitute that lets PublishEventAsync succeed.
    /// PublishEventAsync is abstract on DaprClient, so NSubstitute intercepts it correctly.
    /// </summary>
    private static DaprClient BuildPublishSucceedsDaprClient()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .PublishEventAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<OrderPlacedEvent>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        return daprClient;
    }

    private static IInventoryClient BuildSuccessInventoryClient()
    {
        var client = Substitute.For<IInventoryClient>();
        client
            .ReserveAsync(Arg.Any<ReserveInventoryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReserveInventoryResponse(true, null));
        return client;
    }

    private static IInventoryClient BuildFailingInventoryClient(string reason = "out of stock")
    {
        var client = Substitute.For<IInventoryClient>();
        client
            .ReserveAsync(Arg.Any<ReserveInventoryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReserveInventoryResponse(false, reason));
        return client;
    }

    // -----------------------------------------------------------------------
    // Property 1: Valid order creation returns Pending status with non-empty OrderId
    // Feature: smart-order, Property 1: Valid order creation returns 202 with Pending status
    // Validates: Requirements 1.2, 1.3, 1.4
    // -----------------------------------------------------------------------

    /// <summary>
    /// For any CreateOrderRequest with Quantity > 0 and Price > 0,
    /// CreateOrderAsync must return a non-empty OrderId and Status = Pending.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OrderServiceArbitraries) }, MaxTest = 100)]
    public Property CreateOrderAsync_WhenRequestIsValid_ShouldReturnPendingWithNonEmptyOrderId(
        CreateOrderRequest request)
    {
        var sut = CreateService();

        var response = sut.CreateOrderAsync(request, CancellationToken.None)
            .GetAwaiter().GetResult();

        return (response.Status == OrderStatus.Pending
             && !string.IsNullOrEmpty(response.OrderId))
            .ToProperty();
    }

    /// <summary>
    /// Each call to CreateOrderAsync must produce a unique OrderId.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OrderServiceArbitraries) }, MaxTest = 100)]
    public Property CreateOrderAsync_WhenCalledTwice_ShouldReturnDistinctOrderIds(
        CreateOrderRequest request)
    {
        var sut = CreateService();

        var r1 = sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        var r2 = sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        return (r1.OrderId != r2.OrderId).ToProperty();
    }

    /// <summary>
    /// For any valid request, SaveOrderAsync must be called exactly once with Pending status.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OrderServiceArbitraries) }, MaxTest = 100)]
    public Property CreateOrderAsync_WhenRequestIsValid_ShouldPersistOrderToStateStore(
        CreateOrderRequest request)
    {
        var stateService = Substitute.For<IOrderStateService>();
        var sut = CreateService(stateService: stateService);

        sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        stateService.Received(1)
            .SaveOrderAsync(
                Arg.Is<Order>(o =>
                    o.ProductId == request.ProductId &&
                    o.Quantity == request.Quantity &&
                    o.Status == OrderStatus.Pending),
                Arg.Any<CancellationToken>());

        return true.ToProperty();
    }

    // -----------------------------------------------------------------------
    // Property 2: Invalid order inputs are rejected with ValidationException
    // Feature: smart-order, Property 2: Invalid order inputs are rejected with 400
    // Validates: Requirements 1.2, 1.5
    // -----------------------------------------------------------------------

    /// <summary>
    /// For any request where Quantity &lt;= 0, CreateOrderAsync must throw ValidationException.
    /// The endpoint maps this to HTTP 400.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CreateOrderAsync_WhenQuantityIsZeroOrNegative_ShouldThrowValidationException()
    {
        var gen =
            from productId in Gen.Fresh(() => Guid.NewGuid().ToString("N"))
            from quantity in Gen.Choose(int.MinValue / 2, 0)
            from priceCents in Gen.Choose(1, 100_000)
            select new CreateOrderRequest(productId, quantity, priceCents / 100m);

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var sut = CreateService();
            Exception? thrown = null;
            try { sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { thrown = ex; }
            return (thrown is FluentValidation.ValidationException).ToProperty();
        });
    }

    /// <summary>
    /// For any request where Price &lt;= 0, CreateOrderAsync must throw ValidationException.
    /// The endpoint maps this to HTTP 400.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CreateOrderAsync_WhenPriceIsZeroOrNegative_ShouldThrowValidationException()
    {
        var gen =
            from productId in Gen.Fresh(() => Guid.NewGuid().ToString("N"))
            from quantity in Gen.Choose(1, 100)
            from priceNegCents in Gen.Choose(0, 100_000)
            select new CreateOrderRequest(productId, quantity, -(priceNegCents / 100m));

        return Prop.ForAll(gen.ToArbitrary(), request =>
        {
            var sut = CreateService();
            Exception? thrown = null;
            try { sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { thrown = ex; }
            return (thrown is FluentValidation.ValidationException).ToProperty();
        });
    }

    // -----------------------------------------------------------------------
    // Property 4: Order creation triggers inventory service invocation
    // Feature: smart-order, Property 4: Order creation triggers inventory service invocation
    // Validates: Requirements 2.1, 2.3
    // -----------------------------------------------------------------------

    /// <summary>
    /// For any valid CreateOrderRequest, IInventoryClient.ReserveAsync must be called exactly once
    /// with the correct ProductId, Quantity, and a non-empty generated OrderId.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OrderServiceArbitraries) }, MaxTest = 100)]
    public Property CreateOrderAsync_WhenRequestIsValid_ShouldInvokeInventoryWithCorrectData(
        CreateOrderRequest request)
    {
        var inventoryClient = Substitute.For<IInventoryClient>();
        inventoryClient
            .ReserveAsync(Arg.Any<ReserveInventoryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReserveInventoryResponse(true, null));

        var sut = CreateService(inventoryClient: inventoryClient);
        sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        inventoryClient.Received(1).ReserveAsync(
            Arg.Is<ReserveInventoryRequest>(r =>
                r.ProductId == request.ProductId &&
                r.Quantity == request.Quantity &&
                !string.IsNullOrEmpty(r.OrderId)),
            Arg.Any<CancellationToken>());

        return true.ToProperty();
    }

    // -----------------------------------------------------------------------
    // Property 5: Inventory failure marks order as InventoryFailed
    // Feature: smart-order, Property 5: Inventory failure marks order as InventoryFailed
    // Validates: Requirements 2.7
    // -----------------------------------------------------------------------

    /// <summary>
    /// When IInventoryClient returns Success=false, CreateOrderAsync must return InventoryFailed
    /// and must NOT call PublishEventAsync.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OrderServiceArbitraries) }, MaxTest = 100)]
    public Property CreateOrderAsync_WhenInventoryFails_ShouldReturnInventoryFailedAndNotPublish(
        CreateOrderRequest request)
    {
        var inventoryClient = Substitute.For<IInventoryClient>();
        inventoryClient
            .ReserveAsync(Arg.Any<ReserveInventoryRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ReserveInventoryResponse(false, "out of stock"));

        var stateService = Substitute.For<IOrderStateService>();
        // UpdateOrderStatusAsync must return a valid Order for the InventoryFailed path
        stateService
            .UpdateOrderStatusAsync(Arg.Any<string>(), Arg.Any<OrderStatus>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => new Order(
                callInfo.ArgAt<string>(0),
                request.ProductId,
                request.Quantity,
                request.Price,
                callInfo.ArgAt<OrderStatus>(1),
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow));

        var daprClient = Substitute.For<DaprClient>();
        var sut = CreateService(daprClient: daprClient, inventoryClient: inventoryClient, stateService: stateService);

        var response = sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        // Status must be InventoryFailed
        var statusCorrect = response.Status == OrderStatus.InventoryFailed;

        // PublishEventAsync must never be called
        daprClient.DidNotReceive().PublishEventAsync(
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<OrderPlacedEvent>(),
            Arg.Any<CancellationToken>());

        return statusCorrect.ToProperty();
    }

    // -----------------------------------------------------------------------
    // Property 6: Accepted order publishes OrderPlacedEvent
    // Feature: smart-order, Property 6: Accepted order publishes OrderPlacedEvent
    // Validates: Requirements 3.1, 3.8
    // -----------------------------------------------------------------------

    /// <summary>
    /// When inventory succeeds, PublishEventAsync must be called exactly once with
    /// component "pubsub", topic "order-placed", and an event containing the correct
    /// OrderId, ProductId, Quantity, and Price.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OrderServiceArbitraries) }, MaxTest = 100)]
    public Property CreateOrderAsync_WhenInventorySucceeds_ShouldPublishOrderPlacedEvent(
        CreateOrderRequest request)
    {
        OrderPlacedEvent? capturedEvent = null;

        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .PublishEventAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<OrderPlacedEvent>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedEvent = callInfo.ArgAt<OrderPlacedEvent>(2);
                return Task.CompletedTask;
            });

        var sut = CreateService(daprClient: daprClient);
        var response = sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        // Must have published exactly once to pubsub / order-placed
        daprClient.Received(1).PublishEventAsync(
            "pubsub",
            "order-placed",
            Arg.Any<OrderPlacedEvent>(),
            Arg.Any<CancellationToken>());

        // Event fields must match the request and the generated OrderId
        var eventCorrect =
            capturedEvent is not null &&
            capturedEvent.OrderId == response.OrderId &&
            capturedEvent.ProductId == request.ProductId &&
            capturedEvent.Quantity == request.Quantity;

        return eventCorrect.ToProperty();
    }

    // -----------------------------------------------------------------------
    // Property 11: DaprException on state/publish operation is rethrown
    // Feature: smart-order, Property 11: DaprException on state operation returns HTTP 503
    // Validates: Requirements 4.7, 12.6
    // -----------------------------------------------------------------------

    /// <summary>
    /// When SaveOrderAsync throws DaprException, CreateOrderAsync must rethrow it
    /// (the endpoint layer then maps it to HTTP 503).
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OrderServiceArbitraries) }, MaxTest = 100)]
    public Property CreateOrderAsync_WhenStateStoreSaveThrowsDaprException_ShouldRethrow(
        CreateOrderRequest request)
    {
        var stateService = Substitute.For<IOrderStateService>();
        stateService
            .SaveOrderAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(
                new Dapr.DaprException("statestore unavailable")));

        var sut = CreateService(stateService: stateService);

        Exception? thrown = null;
        try { sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (Exception ex) { thrown = ex; }

        return (thrown is Dapr.DaprException).ToProperty();
    }

    /// <summary>
    /// When PublishEventAsync throws DaprException, CreateOrderAsync must rethrow it.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OrderServiceArbitraries) }, MaxTest = 100)]
    public Property CreateOrderAsync_WhenPublishThrowsDaprException_ShouldRethrow(
        CreateOrderRequest request)
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .PublishEventAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<OrderPlacedEvent>(),
                Arg.Any<CancellationToken>())
            .Returns<Task>(_ => Task.FromException(
                new Dapr.DaprException("pubsub unavailable")));

        var sut = CreateService(daprClient: daprClient);

        Exception? thrown = null;
        try { sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult(); }
        catch (Exception ex) { thrown = ex; }

        return (thrown is Dapr.DaprException).ToProperty();
    }

    // -----------------------------------------------------------------------
    // Property 15: Quantity exceeding max-order-quantity is rejected with 422
    // Feature: smart-order, Property 15: Quantity exceeding max-order-quantity is rejected with 422
    // Validates: Requirements 8.2
    // -----------------------------------------------------------------------

    /// <summary>
    /// For any request where Quantity > MaxOrderQuantity, CreateOrderAsync must throw
    /// OrderQuantityExceededException (the endpoint maps this to HTTP 422).
    /// </summary>
    [Property(MaxTest = 100)]
    public Property CreateOrderAsync_WhenQuantityExceedsMax_ShouldThrowOrderQuantityExceededException()
    {
        // Generate a max between 1 and 50, then a quantity strictly above it
        var gen =
            from max in Gen.Choose(1, 50)
            from excess in Gen.Choose(1, 50)
            from productId in Gen.Fresh(() => Guid.NewGuid().ToString("N"))
            from priceCents in Gen.Choose(1, 100_000)
            select (new CreateOrderRequest(productId, max + excess, priceCents / 100m), max);

        return Prop.ForAll(gen.ToArbitrary(), tuple =>
        {
            var (request, max) = tuple;
            var configService = BuildDefaultConfigService(maxQuantity: max);
            var sut = CreateService(configService: configService);

            Exception? thrown = null;
            try { sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult(); }
            catch (Exception ex) { thrown = ex; }

            return (thrown is OrderQuantityExceededException ex2 &&
                    ex2.RequestedQuantity == request.Quantity &&
                    ex2.MaxQuantity == max)
                .ToProperty();
        });
    }

    // -----------------------------------------------------------------------
    // Property 16: Discount is applied when discount-enabled is true
    // Feature: smart-order, Property 16: Discount is applied when discount-enabled is true
    // Validates: Requirements 8.3
    // -----------------------------------------------------------------------

    /// <summary>
    /// When discount-enabled=true, the price saved to state and published in the event
    /// must be Price * 0.9 (10% discount).
    /// When discount-enabled=false, the price must equal the original Price.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(OrderServiceArbitraries) }, MaxTest = 100)]
    public Property CreateOrderAsync_WhenDiscountEnabled_ShouldApplyTenPercentDiscount(
        CreateOrderRequest request)
    {
        Order? savedOrder = null;
        var stateService = Substitute.For<IOrderStateService>();
        stateService
            .SaveOrderAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                savedOrder = callInfo.ArgAt<Order>(0);
                return Task.CompletedTask;
            });

        var configService = BuildDefaultConfigService(discountEnabled: true);
        var sut = CreateService(stateService: stateService, configService: configService);
        sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        var expectedPrice = request.Price * 0.9m;
        return (savedOrder is not null && savedOrder.Price == expectedPrice).ToProperty();
    }

    [Property(Arbitrary = new[] { typeof(OrderServiceArbitraries) }, MaxTest = 100)]
    public Property CreateOrderAsync_WhenDiscountDisabled_ShouldUseOriginalPrice(
        CreateOrderRequest request)
    {
        Order? savedOrder = null;
        var stateService = Substitute.For<IOrderStateService>();
        stateService
            .SaveOrderAsync(Arg.Any<Order>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                savedOrder = callInfo.ArgAt<Order>(0);
                return Task.CompletedTask;
            });

        var configService = BuildDefaultConfigService(discountEnabled: false);
        var sut = CreateService(stateService: stateService, configService: configService);
        sut.CreateOrderAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        return (savedOrder is not null && savedOrder.Price == request.Price).ToProperty();
    }
}
