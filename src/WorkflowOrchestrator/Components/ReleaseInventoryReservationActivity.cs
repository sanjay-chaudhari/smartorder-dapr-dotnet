using Dapr;
using Dapr.Client;
using Dapr.Workflow;
using WorkflowOrchestrator.Models;

namespace WorkflowOrchestrator.Components;

public class ReleaseInventoryReservationActivity : WorkflowActivity<ReleaseInventoryInput, bool>
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ReleaseInventoryReservationActivity> _logger;

    public ReleaseInventoryReservationActivity(DaprClient daprClient, ILogger<ReleaseInventoryReservationActivity> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public override async Task<bool> RunAsync(WorkflowActivityContext context, ReleaseInventoryInput input)
    {
        _logger.LogInformation(
            "Releasing inventory reservation. OrderId={OrderId}, ProductId={ProductId}, OperationName={OperationName}, ServiceName={ServiceName}",
            input.OrderId, input.ProductId, "ReleaseInventoryReservation", "workflow-orchestrator");

        try
        {
            var request = new { ProductId = input.ProductId, Quantity = input.Quantity, OrderId = input.OrderId };
            await _daprClient
                .InvokeMethodAsync(
                    "inventory-service",
                    "inventory/release",
                    request,
                    CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogInformation("Inventory reservation released. OrderId={OrderId}", input.OrderId);
            return true;
        }
        catch (DaprException ex)
        {
            _logger.LogError(ex,
                "Dapr error releasing inventory. OrderId={OrderId}, ServiceName={ServiceName}, OperationName={OperationName}",
                input.OrderId, "workflow-orchestrator", "ReleaseInventoryReservation");
            return false;
        }
    }
}
