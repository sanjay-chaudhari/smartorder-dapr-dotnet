using Dapr;
using Dapr.Client;
using Dapr.Workflow;
using WorkflowOrchestrator.Models;

namespace WorkflowOrchestrator.Components;

public class RefundPaymentActivity : WorkflowActivity<RefundPaymentInput, bool>
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<RefundPaymentActivity> _logger;

    public RefundPaymentActivity(DaprClient daprClient, ILogger<RefundPaymentActivity> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public override async Task<bool> RunAsync(WorkflowActivityContext context, RefundPaymentInput input)
    {
        _logger.LogInformation(
            "Refunding payment. OrderId={OrderId}, TransactionId={TransactionId}, OperationName={OperationName}, ServiceName={ServiceName}",
            input.OrderId, input.TransactionId, "RefundPayment", "workflow-orchestrator");

        try
        {
            var request = new { OrderId = input.OrderId, TransactionId = input.TransactionId, Amount = input.Amount };
            await _daprClient
                .InvokeMethodAsync(
                    "payment-service",
                    "payments/refund",
                    request,
                    CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogInformation("Payment refunded. OrderId={OrderId}, TransactionId={TransactionId}",
                input.OrderId, input.TransactionId);
            return true;
        }
        catch (DaprException ex)
        {
            _logger.LogError(ex,
                "Dapr error refunding payment. OrderId={OrderId}, ServiceName={ServiceName}, OperationName={OperationName}",
                input.OrderId, "workflow-orchestrator", "RefundPayment");
            return false;
        }
    }
}
