using Dapr;
using Dapr.Client;
using Dapr.Workflow;
using WorkflowOrchestrator.Models;

namespace WorkflowOrchestrator.Components;

public class SendNotificationActivity : WorkflowActivity<SendNotificationInput, NotificationResult>
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<SendNotificationActivity> _logger;

    public SendNotificationActivity(DaprClient daprClient, ILogger<SendNotificationActivity> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public override async Task<NotificationResult> RunAsync(WorkflowActivityContext context, SendNotificationInput input)
    {
        _logger.LogInformation(
            "Sending notification. OrderId={OrderId}, CustomerId={CustomerId}, OperationName={OperationName}, ServiceName={ServiceName}",
            input.OrderId, input.CustomerId, "SendNotification", "workflow-orchestrator");

        try
        {
            var request = new { OrderId = input.OrderId, CustomerId = input.CustomerId, Message = input.Message };
            await _daprClient
                .InvokeMethodAsync(
                    "notification-service",
                    "notifications/send",
                    request,
                    CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogInformation("Notification sent. OrderId={OrderId}", input.OrderId);
            return new NotificationResult(true);
        }
        catch (DaprException ex)
        {
            _logger.LogError(ex,
                "Dapr error sending notification. OrderId={OrderId}, ServiceName={ServiceName}, OperationName={OperationName}",
                input.OrderId, "workflow-orchestrator", "SendNotification");
            // Notification failure is non-fatal — saga still succeeds
            return new NotificationResult(false);
        }
    }
}
