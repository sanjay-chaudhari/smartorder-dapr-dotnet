namespace OrderService.Models;

public record ReserveInventoryRequest(string ProductId, int Quantity, string OrderId);
public record ReserveInventoryResponse(bool Success, string? FailureReason);
