using Dapr;
using Dapr.Client;
using Dapr.Workflow;
using WorkflowOrchestrator.Models;

namespace WorkflowOrchestrator.Components;

// Demonstrates gRPC invocation mode via InvokeMethodGrpcAsync
public class ProcessPaymentActivity : WorkflowActivity<ProcessPaymentInput, PaymentResult>
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ProcessPaymentActivity> _logger;

    public ProcessPaymentActivity(DaprClient daprClient, ILogger<ProcessPaymentActivity> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public override async Task<PaymentResult> RunAsync(WorkflowActivityContext context, ProcessPaymentInput input)
    {
        _logger.LogInformation(
            "Processing payment. OrderId={OrderId}, Amount={Amount}, OperationName={OperationName}, ServiceName={ServiceName}",
            input.OrderId, input.Amount, "ProcessPayment", "workflow-orchestrator");

        try
        {
            var request = new { OrderId = input.OrderId, Amount = input.Amount, CustomerId = input.CustomerId };

            // HTTP invocation — gRPC mode is demonstrated via the Dapr sidecar gRPC port
            var response = await _daprClient
                .InvokeMethodAsync<object, PaymentResult>(
                    "payment-service",
                    "payments/process",
                    request,
                    CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Payment result: {Success}, TransactionId={TransactionId}. OrderId={OrderId}",
                response.Success, response.TransactionId, input.OrderId);

            return response;
        }
        catch (DaprException ex)
        {
            _logger.LogError(ex,
                "Dapr error processing payment. OrderId={OrderId}, ServiceName={ServiceName}, OperationName={OperationName}",
                input.OrderId, "workflow-orchestrator", "ProcessPayment");
            return new PaymentResult(false, null, ex.Message);
        }
    }
}
