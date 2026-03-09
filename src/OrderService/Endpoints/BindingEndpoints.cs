using System.Diagnostics;
using Dapr.Client;
using OrderService.Services;

namespace OrderService.Endpoints;

public record WebhookResult(string OrderId, bool WebhookSent);

public static class BindingEndpoints
{
    public static void MapBindingEndpoints(this WebApplication app)
    {
        // Input binding — Dapr cron fires POST /orders/cleanup every 5 minutes.
        // The route name must match the component metadata name: "order-cleanup-cron".
        app.MapPost("/order-cleanup-cron", async (
            DaprClient daprClient,
            IOrderStateService stateService,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation(
                "Cron binding triggered: starting stale order cleanup. OperationName={OperationName}, ServiceName={ServiceName}, TraceId={TraceId}",
                "CronCleanup", "order-service", Activity.Current?.TraceId.ToString() ?? string.Empty);

            logger.LogInformation("Cron cleanup completed. OperationName={OperationName}", "CronCleanup");

            return Results.Ok();
        });

        // Output binding — send an order-confirmed webhook via the HTTP output binding.
        app.MapPost("/orders/{orderId}/notify-webhook", async (
            string orderId,
            DaprClient daprClient,
            ILogger<Program> logger,
            CancellationToken cancellationToken) =>
        {
            logger.LogInformation(
                "Sending order-confirmed webhook via output binding. OrderId={OrderId}, OperationName={OperationName}, ServiceName={ServiceName}, TraceId={TraceId}",
                orderId, "OutputBinding", "order-service", Activity.Current?.TraceId.ToString() ?? string.Empty);

            try
            {
                var payload = new { orderId, status = "confirmed", timestamp = DateTimeOffset.UtcNow };

                await daprClient.InvokeBindingAsync(
                    "order-webhook",
                    "create",
                    payload,
                    cancellationToken: cancellationToken).ConfigureAwait(false);

                logger.LogInformation(
                    "Webhook sent successfully for order {OrderId}. OperationName={OperationName}",
                    orderId, "OutputBinding");

                return Results.Ok(new WebhookResult(orderId, true));
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "Failed to send webhook for order {OrderId}. OperationName={OperationName}, ServiceName={ServiceName}",
                    orderId, "OutputBinding", "order-service");
                return Results.Problem(statusCode: 503, title: "Webhook delivery failed");
            }
        });
    }
}
