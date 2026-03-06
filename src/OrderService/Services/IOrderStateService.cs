using OrderService.Models;

namespace OrderService.Services;

public interface IOrderStateService
{
    Task SaveOrderAsync(Order order, CancellationToken cancellationToken);
    Task<Order?> GetOrderAsync(string orderId, CancellationToken cancellationToken);
    Task<Order> UpdateOrderStatusAsync(string orderId, OrderStatus newStatus, CancellationToken cancellationToken);
}
