using Dapr;
using InventoryService.Models;
using InventoryService.Services;

namespace InventoryService.Endpoints;

public static class InventoryEndpoints
{
    public static void MapInventoryEndpoints(this WebApplication app)
    {
        app.MapPost("/inventory/reserve", async (
            ReserveInventoryRequest request,
            IInventoryService inventoryService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("InventoryEndpoints");
            try
            {
                var result = await inventoryService.ReserveAsync(request, cancellationToken).ConfigureAwait(false);
                return result.Success
                    ? Results.Ok(result)
                    : Results.UnprocessableEntity(result);
            }
            catch (DaprException ex)
            {
                logger.LogError(ex, "Dapr error in POST /inventory/reserve for {ProductId}", request.ProductId);
                return Results.Problem(statusCode: 503, title: "Service unavailable");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in POST /inventory/reserve");
                return Results.Problem();
            }
        });

        app.MapPost("/inventory/release", async (
            ReleaseInventoryRequest request,
            IInventoryService inventoryService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("InventoryEndpoints");
            try
            {
                var result = await inventoryService.ReleaseAsync(request, cancellationToken).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (DaprException ex)
            {
                logger.LogError(ex, "Dapr error in POST /inventory/release for {ProductId}", request.ProductId);
                return Results.Problem(statusCode: 503, title: "Service unavailable");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in POST /inventory/release");
                return Results.Problem();
            }
        });

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "inventory-service" }));
    }
}
