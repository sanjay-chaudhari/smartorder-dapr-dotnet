using Dapr;
using Dapr.AspNetCore;
using NotificationService.Models;
using NotificationService.Services;

namespace NotificationService.Endpoints;

public static class NotificationEndpoints
{
    public static void MapNotificationEndpoints(this WebApplication app)
    {
        // Pub/sub subscriber for order-placed topic
        app.MapPost("/subscribe/order-placed",
            [Topic("pubsub", "order-placed", "order-placed-deadletter", false)]
            async (
                OrderPlacedEvent orderEvent,
                INotificationService notificationService,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("NotificationEndpoints");
                try
                {
                    logger.LogInformation(
                        "Received order-placed event for {OrderId}", orderEvent.OrderId);

                    var request = new SendNotificationRequest(
                        orderEvent.OrderId,
                        "customer-from-event",
                        $"Your order {orderEvent.OrderId} has been placed successfully.");

                    await notificationService
                        .SendNotificationAsync(request, cancellationToken)
                        .ConfigureAwait(false);

                    return Results.Ok();
                }
                catch (DaprException ex)
                {
                    logger.LogError(ex,
                        "Dapr error processing order-placed for {OrderId}", orderEvent.OrderId);
                    // Return 500 to trigger retry
                    return Results.StatusCode(500);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Unhandled error processing order-placed for {OrderId}", orderEvent.OrderId);
                    return Results.StatusCode(500);
                }
            });

        // Direct invocation endpoint for workflow orchestrator
        app.MapPost("/notifications/send",
            async (
                SendNotificationRequest request,
                INotificationService notificationService,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("NotificationEndpoints");
                try
                {
                    logger.LogInformation(
                        "Direct notification request for {OrderId}", request.OrderId);

                    await notificationService
                        .SendNotificationAsync(request, cancellationToken)
                        .ConfigureAwait(false);

                    return Results.Ok();
                }
                catch (DaprException ex)
                {
                    logger.LogError(ex,
                        "Dapr error sending notification for {OrderId}", request.OrderId);
                    return Results.Problem("Failed to send notification");
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "Unhandled error sending notification for {OrderId}", request.OrderId);
                    return Results.Problem("Unexpected error");
                }
            });

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "notification-service" }));
    }
}
