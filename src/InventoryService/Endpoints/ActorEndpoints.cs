using Dapr.Actors;
using Dapr.Actors.Client;
using InventoryService.Actors;
using InventoryService.Models;

namespace InventoryService.Endpoints;

/// <summary>
/// Exposes actor-backed inventory operations.
/// Actor ID = productId — the runtime routes all calls for the same product
/// to the same actor instance, serializing access without explicit locks.
/// </summary>
public static class ActorEndpoints
{
    public static void MapActorEndpoints(this WebApplication app)
    {
        // Reserve via actor
        app.MapPost("/inventory/actor/reserve", async (
            ReserveInventoryRequest request,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("ActorEndpoints");
            try
            {
                var actor = ActorProxy.Create<IOrderActor>(
                    new ActorId(request.ProductId),
                    nameof(OrderActor));

                var result = await actor.ReserveAsync(request, cancellationToken).ConfigureAwait(false);
                return result.Success ? Results.Ok(result) : Results.UnprocessableEntity(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in actor reserve for {ProductId}", request.ProductId);
                return Results.Problem();
            }
        });

        // Release via actor
        app.MapPost("/inventory/actor/release", async (
            ReleaseInventoryRequest request,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("ActorEndpoints");
            try
            {
                var actor = ActorProxy.Create<IOrderActor>(
                    new ActorId(request.ProductId),
                    nameof(OrderActor));

                var result = await actor.ReleaseAsync(request, cancellationToken).ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in actor release for {ProductId}", request.ProductId);
                return Results.Problem();
            }
        });

        // Get stock via actor
        app.MapGet("/inventory/actor/{productId}/stock", async (
            string productId,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("ActorEndpoints");
            try
            {
                var actor = ActorProxy.Create<IOrderActor>(
                    new ActorId(productId),
                    nameof(OrderActor));

                var stock = await actor.GetStockAsync(cancellationToken).ConfigureAwait(false);
                return stock is null ? Results.NotFound() : Results.Ok(stock);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error getting actor stock for {ProductId}", productId);
                return Results.Problem();
            }
        });

        // Seed stock via actor (for testing / initial data load)
        app.MapPut("/inventory/actor/{productId}/stock", async (
            string productId,
            InventoryItem item,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("ActorEndpoints");
            try
            {
                var actor = ActorProxy.Create<IOrderActor>(
                    new ActorId(productId),
                    nameof(OrderActor));

                await actor.SeedStockAsync(item, cancellationToken).ConfigureAwait(false);
                return Results.Ok();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error seeding actor stock for {ProductId}", productId);
                return Results.Problem();
            }
        });
    }
}
