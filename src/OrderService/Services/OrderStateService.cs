using Dapr;
using Dapr.Client;
using OrderService.Models;

namespace OrderService.Services;

public class OrderStateService : IOrderStateService
{
    private const string StateStore = "statestore";
    private readonly DaprClient _daprClient;
    private readonly ILogger<OrderStateService> _logger;

    private static readonly StateOptions StrongConsistency = new()
    {
        Consistency = ConsistencyMode.Strong
    };

    public OrderStateService(DaprClient daprClient, ILogger<OrderStateService> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public async Task SaveOrderAsync(Order order, CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "order-service",
            ["OperationName"] = "SaveOrder",
            ["OrderId"] = order.OrderId
        }))
        {
            try
            {
                _logger.LogInformation("Saving order to state store");

                var key = $"order-{order.OrderId}";
                var operations = new List<StateTransactionRequest>
                {
                    new(key, System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(order),
                        StateOperationType.Upsert,
                        options: StrongConsistency)
                };

                await _daprClient.ExecuteStateTransactionAsync(StateStore, operations, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation("Order saved successfully");
            }
            catch (DaprException ex)
            {
                _logger.LogError(ex, "Failed to save order to state store");
                throw;
            }
        }
    }

    public async Task<Order?> GetOrderAsync(string orderId, CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "order-service",
            ["OperationName"] = "GetOrder",
            ["OrderId"] = orderId
        }))
        {
            try
            {
                _logger.LogInformation("Retrieving order from state store");

                var key = $"order-{orderId}";
                var (state, _) = await _daprClient.GetStateAndETagAsync<Order>(StateStore, key, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                return state;
            }
            catch (DaprException ex)
            {
                _logger.LogError(ex, "Failed to retrieve order from state store");
                throw;
            }
        }
    }

    public async Task<Order> UpdateOrderStatusAsync(string orderId, OrderStatus newStatus, CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "order-service",
            ["OperationName"] = "UpdateOrderStatus",
            ["OrderId"] = orderId
        }))
        {
            try
            {
                _logger.LogInformation("Updating order status to {NewStatus}", newStatus);

                var key = $"order-{orderId}";

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    var (state, etag) = await _daprClient.GetStateAndETagAsync<Order>(StateStore, key, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    var updated = state with { Status = newStatus, UpdatedAt = DateTimeOffset.UtcNow };

                    var saved = await _daprClient.TrySaveStateAsync(StateStore, key, updated, etag, StrongConsistency, cancellationToken: cancellationToken)
                        .ConfigureAwait(false);

                    if (saved)
                    {
                        _logger.LogInformation("Order status updated successfully on attempt {Attempt}", attempt + 1);
                        return updated;
                    }

                    _logger.LogWarning("ETag conflict on attempt {Attempt}, retrying", attempt + 1);
                }

                throw new ConcurrencyException($"ETag conflict after 3 retries for order {orderId}");
            }
            catch (DaprException ex)
            {
                _logger.LogError(ex, "Failed to update order status in state store");
                throw;
            }
        }
    }
}
