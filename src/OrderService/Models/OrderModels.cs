namespace OrderService.Models;

public enum OrderStatus
{
    Pending,
    InventoryReserved,
    PaymentProcessed,
    Completed,
    InventoryFailed,
    PaymentFailed,
    Failed
}

public record CreateOrderRequest(string ProductId, int Quantity, decimal Price);

public record CreateOrderResponse(string OrderId, OrderStatus Status);

public record OrderResponse(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal Price,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record Order(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal Price,
    OrderStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt);

public record OrderPlacedEvent(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal Price,
    DateTimeOffset PlacedAt);
