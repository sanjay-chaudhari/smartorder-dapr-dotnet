using Dapr;
using Dapr.Client;
using InventoryService.Models;

namespace InventoryService.Services;

public class InventoryService : IInventoryService
{
    private const string StateStore = "statestore";

    private static readonly StateOptions StrongConsistency = new()
    {
        Consistency = ConsistencyMode.Strong
    };

    private readonly DaprClient _daprClient;
    private readonly ILogger<InventoryService> _logger;

    public InventoryService(DaprClient daprClient, ILogger<InventoryService> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public async Task<ReserveInventoryResponse> ReserveAsync(
        ReserveInventoryRequest request,
        CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "inventory-service",
            ["OperationName"] = "ReserveInventory",
            ["OrderId"] = request.OrderId
        }))
        {
            try
            {
                _logger.LogInformation(
                    "Reserving {Quantity} units of {ProductId} for order {OrderId}",
                    request.Quantity, request.ProductId, request.OrderId);

                var key = $"inventory-{request.ProductId}";
                var (item, etag) = await _daprClient
                    .GetStateAndETagAsync<InventoryItem>(StateStore, key, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (item is null)
                {
                    _logger.LogWarning("Product {ProductId} not found in inventory", request.ProductId);
                    return new ReserveInventoryResponse(false, $"Product {request.ProductId} not found");
                }

                if (item.AvailableQuantity < request.Quantity)
                {
                    _logger.LogWarning(
                        "Insufficient stock for {ProductId}: available={Available}, requested={Requested}",
                        request.ProductId, item.AvailableQuantity, request.Quantity);
                    return new ReserveInventoryResponse(false,
                        $"Insufficient stock: available={item.AvailableQuantity}, requested={request.Quantity}");
                }

                var updated = item with
                {
                    AvailableQuantity = item.AvailableQuantity - request.Quantity,
                    ReservedQuantity = item.ReservedQuantity + request.Quantity
                };

                var saved = await _daprClient
                    .TrySaveStateAsync(StateStore, key, updated, etag, StrongConsistency, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!saved)
                {
                    _logger.LogWarning("Concurrent update conflict for {ProductId}, reservation failed", request.ProductId);
                    return new ReserveInventoryResponse(false, "Concurrent update conflict, please retry");
                }

                _logger.LogInformation(
                    "Reserved {Quantity} units of {ProductId} successfully", request.Quantity, request.ProductId);
                return new ReserveInventoryResponse(true, null);
            }
            catch (DaprException ex)
            {
                _logger.LogError(ex,
                    "Dapr error reserving inventory for {ProductId}. ServiceName={ServiceName}, OperationName={OperationName}",
                    request.ProductId, "inventory-service", "ReserveInventory");
                throw;
            }
        }
    }

    public async Task<ReleaseInventoryResponse> ReleaseAsync(
        ReleaseInventoryRequest request,
        CancellationToken cancellationToken)
    {
        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "inventory-service",
            ["OperationName"] = "ReleaseInventory",
            ["OrderId"] = request.OrderId
        }))
        {
            try
            {
                _logger.LogInformation(
                    "Releasing {Quantity} units of {ProductId} for order {OrderId}",
                    request.Quantity, request.ProductId, request.OrderId);

                var key = $"inventory-{request.ProductId}";
                var (item, etag) = await _daprClient
                    .GetStateAndETagAsync<InventoryItem>(StateStore, key, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (item is null)
                {
                    _logger.LogWarning("Product {ProductId} not found during release", request.ProductId);
                    return new ReleaseInventoryResponse(false);
                }

                var updated = item with
                {
                    AvailableQuantity = item.AvailableQuantity + request.Quantity,
                    ReservedQuantity = Math.Max(0, item.ReservedQuantity - request.Quantity)
                };

                var saved = await _daprClient
                    .TrySaveStateAsync(StateStore, key, updated, etag, StrongConsistency, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);

                if (!saved)
                {
                    _logger.LogWarning("Concurrent update conflict during release for {ProductId}", request.ProductId);
                    return new ReleaseInventoryResponse(false);
                }

                _logger.LogInformation(
                    "Released {Quantity} units of {ProductId} successfully", request.Quantity, request.ProductId);
                return new ReleaseInventoryResponse(true);
            }
            catch (DaprException ex)
            {
                _logger.LogError(ex,
                    "Dapr error releasing inventory for {ProductId}. ServiceName={ServiceName}, OperationName={OperationName}",
                    request.ProductId, "inventory-service", "ReleaseInventory");
                throw;
            }
        }
    }
}
