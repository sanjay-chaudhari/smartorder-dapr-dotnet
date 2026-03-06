using Dapr;
using Dapr.Client;
using FluentValidation;
using OrderService.Models;
using OrderService.Services;

namespace OrderService.Endpoints;

public static class OrderEndpoints
{
    public static void MapOrderEndpoints(this WebApplication app)
    {
        app.MapPost("/orders", async (CreateOrderRequest request, IOrderService orderService, CancellationToken ct) =>
        {
            try
            {
                var result = await orderService.CreateOrderAsync(request, ct);
                return Results.Accepted($"/orders/{result.OrderId}", result);
            }
            catch (ValidationException ex)
            {
                var errors = ex.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray());
                return Results.ValidationProblem(errors);
            }
            catch (OrderQuantityExceededException)
            {
                return Results.Problem(statusCode: 422, title: "Order quantity exceeded");
            }
            catch (DaprException)
            {
                return Results.Problem(statusCode: 503, title: "Service unavailable");
            }
            catch (Exception)
            {
                return Results.Problem();
            }
        });

        app.MapGet("/orders/{orderId}", async (string orderId, IOrderStateService stateService, CancellationToken ct) =>
        {
            try
            {
                var order = await stateService.GetOrderAsync(orderId, ct);
                if (order is null) return Results.NotFound();
                return Results.Ok(new OrderResponse(
                    order.OrderId,
                    order.ProductId,
                    order.Quantity,
                    order.Price,
                    order.Status,
                    order.CreatedAt,
                    order.UpdatedAt));
            }
            catch (DaprException)
            {
                return Results.Problem(statusCode: 503, title: "Service unavailable");
            }
            catch (Exception)
            {
                return Results.Problem();
            }
        });
    }
}
