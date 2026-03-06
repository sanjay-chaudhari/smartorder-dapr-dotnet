using InventoryService.Models;

namespace InventoryService.Services;

public interface IInventoryService
{
    Task<ReserveInventoryResponse> ReserveAsync(ReserveInventoryRequest request, CancellationToken cancellationToken);
    Task<ReleaseInventoryResponse> ReleaseAsync(ReleaseInventoryRequest request, CancellationToken cancellationToken);
}
