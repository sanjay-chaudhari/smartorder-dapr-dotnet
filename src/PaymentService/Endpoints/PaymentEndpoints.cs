using Dapr;
using Dapr.AspNetCore;
using PaymentService.Models;
using PaymentService.Services;

namespace PaymentService.Endpoints;

public static class PaymentEndpoints
{
    public static void MapPaymentEndpoints(this WebApplication app)
    {
        // gRPC/HTTP invocation target from WorkflowOrchestrator
        app.MapPost("/payments/process", async (
            ProcessPaymentRequest request,
            IPaymentService paymentService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("PaymentEndpoints");
            try
            {
                var result = await paymentService
                    .ProcessPaymentAsync(request, cancellationToken)
                    .ConfigureAwait(false);
                return Results.Ok(result);
            }
            catch (DaprException ex)
            {
                logger.LogError(ex, "Dapr error in POST /payments/process for {OrderId}", request.OrderId);
                return Results.Problem(statusCode: 503, title: "Service unavailable");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error in POST /payments/process");
                return Results.Problem();
            }
        });

        // Pub/sub subscriber for order-placed topic
        app.MapPost("/subscribe/order-placed",
            [Topic("pubsub", "order-placed", "order-placed-deadletter", false)]
            async (
                OrderPlacedEvent orderEvent,
                IPaymentService paymentService,
                ILoggerFactory loggerFactory,
                CancellationToken cancellationToken) =>
            {
                var logger = loggerFactory.CreateLogger("PaymentEndpoints");
                try
                {
                    logger.LogInformation(
                        "Received order-placed event for {OrderId}", orderEvent.OrderId);

                    var request = new ProcessPaymentRequest(
                        orderEvent.OrderId,
                        orderEvent.Price * orderEvent.Quantity,
                        "customer-from-event");

                    var result = await paymentService
                        .ProcessPaymentAsync(request, cancellationToken)
                        .ConfigureAwait(false);

                    if (!result.Success)
                    {
                        logger.LogWarning(
                            "Payment failed for {OrderId}: {Reason}", orderEvent.OrderId, result.FailureReason);
                        // Return 404 to drop the message — payment failure is a business decision, not a retry scenario
                        return Results.NotFound();
                    }

                    return Results.Ok();
                }
                catch (DaprException ex)
                {
                    logger.LogError(ex, "Dapr error processing order-placed for {OrderId}", orderEvent.OrderId);
                    // Return 500 to trigger retry
                    return Results.StatusCode(500);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled error processing order-placed for {OrderId}", orderEvent.OrderId);
                    return Results.StatusCode(500);
                }
            });

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "payment-service" }));
    }
}
