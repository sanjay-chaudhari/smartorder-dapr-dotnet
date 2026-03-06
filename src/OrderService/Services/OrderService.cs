using System.Diagnostics;
using Dapr;
using Dapr.Client;
using FluentValidation;
using OrderService.Models;

namespace OrderService.Services;

public class OrderService : IOrderService
{
    private readonly DaprClient _daprClient;
    private readonly IInventoryClient _inventoryClient;
    private readonly IOrderStateService _orderStateService;
    private readonly IConfigurationService _configurationService;
    private readonly ILogger<OrderService> _logger;
    private readonly OrderValidator _validator;

    public OrderService(
        DaprClient daprClient,
        IInventoryClient inventoryClient,
        IOrderStateService orderStateService,
        IConfigurationService configurationService,
        ILogger<OrderService> logger,
        OrderValidator validator)
    {
        _daprClient = daprClient;
        _inventoryClient = inventoryClient;
        _orderStateService = orderStateService;
        _configurationService = configurationService;
        _logger = logger;
        _validator = validator;
    }

    public async Task<CreateOrderResponse> CreateOrderAsync(
        CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        // Step 1: FluentValidation
        var validationResult = await _validator.ValidateAsync(request, cancellationToken).ConfigureAwait(false);
        if (!validationResult.IsValid)
            throw new ValidationException(validationResult.Errors);

        // Step 2: Check max-order-quantity
        if (request.Quantity > _configurationService.MaxOrderQuantity)
            throw new OrderQuantityExceededException(request.Quantity, _configurationService.MaxOrderQuantity);

        // Step 3: Apply discount if enabled
        var price = _configurationService.DiscountEnabled ? request.Price * 0.9m : request.Price;

        // Step 4: Generate OrderId and create Order
        var orderId = Guid.NewGuid().ToString("N");
        var order = new Order(
            orderId,
            request.ProductId,
            request.Quantity,
            price,
            OrderStatus.Pending,
            DateTimeOffset.UtcNow,
            null);

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "order-service",
            ["OperationName"] = "CreateOrder",
            ["OrderId"] = orderId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? string.Empty,
            ["SpanId"] = Activity.Current?.SpanId.ToString() ?? string.Empty
        }))
        {
            // Step 5: Save to state store
            await _orderStateService.SaveOrderAsync(order, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Order {OrderId} created and saved to state store", orderId);

            // Step 6: Invoke InventoryService via Dapr service invocation
            var reserveRequest = new ReserveInventoryRequest(request.ProductId, request.Quantity, orderId);
            ReserveInventoryResponse reserveResponse;
            try
            {
                reserveResponse = await _inventoryClient.ReserveAsync(reserveRequest, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (DaprException ex)
            {
                _logger.LogError(ex,
                    "Dapr error invoking inventory-service for {OrderId}. ServiceName={ServiceName}, OperationName={OperationName}",
                    orderId, "order-service", "InvokeInventory");
                throw;
            }

            // Step 7: Handle inventory failure
            if (!reserveResponse.Success)
            {
                _logger.LogWarning(
                    "Inventory reservation failed for {OrderId}. Reason: {FailureReason}",
                    orderId, reserveResponse.FailureReason);

                await _orderStateService.UpdateOrderStatusAsync(
                    orderId, OrderStatus.InventoryFailed, cancellationToken).ConfigureAwait(false);

                return new CreateOrderResponse(orderId, OrderStatus.InventoryFailed);
            }

            _logger.LogInformation("Inventory reserved successfully for {OrderId}", orderId);

            // Step 8: Publish OrderPlacedEvent
            var orderPlacedEvent = new OrderPlacedEvent(
                orderId,
                request.ProductId,
                request.Quantity,
                price,
                DateTimeOffset.UtcNow);

            try
            {
                await _daprClient.PublishEventAsync(
                    "pubsub",
                    "order-placed",
                    orderPlacedEvent,
                    cancellationToken).ConfigureAwait(false);
            }
            catch (DaprException ex)
            {
                _logger.LogError(ex,
                    "Dapr error publishing order-placed event for {OrderId}. ServiceName={ServiceName}, OperationName={OperationName}",
                    orderId, "order-service", "PublishOrderPlaced");
                throw;
            }

            _logger.LogInformation("OrderPlacedEvent published for {OrderId}", orderId);

            // Step 9: Return success
            return new CreateOrderResponse(orderId, OrderStatus.Pending);
        }
    }
}
