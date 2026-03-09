using Dapr.Actors;
using InventoryService.Models;

namespace InventoryService.Actors;

/// <summary>
/// Dapr Virtual Actor — one instance per product, serializes all reservation/release
/// operations for that product without explicit locking.
/// </summary>
public interface IOrderActor : IActor
{
    Task<ReserveInventoryResponse> ReserveAsync(ReserveInventoryRequest request, CancellationToken cancellationToken);
    Task<ReleaseInventoryResponse> ReleaseAsync(ReleaseInventoryRequest request, CancellationToken cancellationToken);
    Task<InventoryItem?> GetStockAsync(CancellationToken cancellationToken);
    Task SeedStockAsync(InventoryItem item, CancellationToken cancellationToken);
}
