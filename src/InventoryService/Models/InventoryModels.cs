namespace InventoryService.Models;

public record ReserveInventoryRequest(string ProductId, int Quantity, string OrderId);

public record ReserveInventoryResponse(bool Success, string? FailureReason);

public record ReleaseInventoryRequest(string ProductId, int Quantity, string OrderId);

public record ReleaseInventoryResponse(bool Success);

// State store record — key: inventory-{productId}
public record InventoryItem(string ProductId, int AvailableQuantity, int ReservedQuantity);
