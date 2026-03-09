using Dapr.Actors.Runtime;
using InventoryService.Models;

namespace InventoryService.Actors;

/// <summary>
/// Virtual Actor that manages per-product inventory state.
/// Actor ID = productId — guarantees single-threaded access per product,
/// eliminating the need for ETag-based optimistic concurrency retries.
/// State is persisted automatically by the Dapr actor runtime via the statestore.
/// </summary>
public class OrderActor : Actor, IOrderActor
{
    private const string InventoryStateKey = "inventory-state";
    private readonly ILogger<OrderActor> _logger;

    public OrderActor(ActorHost host, ILogger<OrderActor> logger) : base(host)
    {
        _logger = logger;
    }

    public async Task<ReserveInventoryResponse> ReserveAsync(
        ReserveInventoryRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Actor {ActorId} reserving {Quantity} units for order {OrderId}. OperationName={OperationName}, ServiceName={ServiceName}",
            Id.GetId(), request.Quantity, request.OrderId, "ActorReserve", "inventory-service");

        var item = await StateManager.GetOrAddStateAsync(
            InventoryStateKey,
            new InventoryItem(request.ProductId, 0, 0),
            cancellationToken).ConfigureAwait(false);

        if (item.AvailableQuantity < request.Quantity)
        {
            _logger.LogWarning(
                "Actor {ActorId}: insufficient stock. Available={Available}, Requested={Requested}",
                Id.GetId(), item.AvailableQuantity, request.Quantity);
            return new ReserveInventoryResponse(false,
                $"Insufficient stock: available={item.AvailableQuantity}, requested={request.Quantity}");
        }

        var updated = item with
        {
            AvailableQuantity = item.AvailableQuantity - request.Quantity,
            ReservedQuantity = item.ReservedQuantity + request.Quantity
        };

        await StateManager.SetStateAsync(InventoryStateKey, updated, cancellationToken).ConfigureAwait(false);
        await StateManager.SaveStateAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Actor {ActorId}: reserved {Quantity} units. Remaining={Remaining}",
            Id.GetId(), request.Quantity, updated.AvailableQuantity);

        return new ReserveInventoryResponse(true, null);
    }

    public async Task<ReleaseInventoryResponse> ReleaseAsync(
        ReleaseInventoryRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Actor {ActorId} releasing {Quantity} units for order {OrderId}. OperationName={OperationName}, ServiceName={ServiceName}",
            Id.GetId(), request.Quantity, request.OrderId, "ActorRelease", "inventory-service");

        var item = await StateManager.TryGetStateAsync<InventoryItem>(InventoryStateKey, cancellationToken)
            .ConfigureAwait(false);

        if (!item.HasValue)
        {
            _logger.LogWarning("Actor {ActorId}: no state found during release", Id.GetId());
            return new ReleaseInventoryResponse(false);
        }

        var updated = item.Value with
        {
            AvailableQuantity = item.Value.AvailableQuantity + request.Quantity,
            ReservedQuantity = Math.Max(0, item.Value.ReservedQuantity - request.Quantity)
        };

        await StateManager.SetStateAsync(InventoryStateKey, updated, cancellationToken).ConfigureAwait(false);
        await StateManager.SaveStateAsync(cancellationToken).ConfigureAwait(false);

        _logger.LogInformation(
            "Actor {ActorId}: released {Quantity} units. Available={Available}",
            Id.GetId(), request.Quantity, updated.AvailableQuantity);

        return new ReleaseInventoryResponse(true);
    }

    public async Task<InventoryItem?> GetStockAsync(CancellationToken cancellationToken)
    {
        var result = await StateManager.TryGetStateAsync<InventoryItem>(InventoryStateKey, cancellationToken)
            .ConfigureAwait(false);
        return result.HasValue ? result.Value : null;
    }

    public async Task SeedStockAsync(InventoryItem item, CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Actor {ActorId} seeding stock: Available={Available}. OperationName={OperationName}, ServiceName={ServiceName}",
            Id.GetId(), item.AvailableQuantity, "ActorSeedStock", "inventory-service");

        await StateManager.SetStateAsync(InventoryStateKey, item, cancellationToken).ConfigureAwait(false);
        await StateManager.SaveStateAsync(cancellationToken).ConfigureAwait(false);
    }
}
