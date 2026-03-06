using Dapr;
using Dapr.Client;
using Dapr.Workflow;
using WorkflowOrchestrator.Models;

namespace WorkflowOrchestrator.Components;

public class ReserveInventoryActivity : WorkflowActivity<ReserveInventoryInput, ReservationResult>
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<ReserveInventoryActivity> _logger;

    public ReserveInventoryActivity(DaprClient daprClient, ILogger<ReserveInventoryActivity> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public override async Task<ReservationResult> RunAsync(WorkflowActivityContext context, ReserveInventoryInput input)
    {
        _logger.LogInformation(
            "Reserving inventory. OrderId={OrderId}, ProductId={ProductId}, OperationName={OperationName}, ServiceName={ServiceName}",
            input.OrderId, input.ProductId, "ReserveInventory", "workflow-orchestrator");

        try
        {
            var request = new { ProductId = input.ProductId, Quantity = input.Quantity, OrderId = input.OrderId };
            var response = await _daprClient
                .InvokeMethodAsync<object, ReservationResult>(
                    "inventory-service",
                    "inventory/reserve",
                    request,
                    CancellationToken.None)
                .ConfigureAwait(false);

            _logger.LogInformation(
                "Inventory reservation result: {Success}. OrderId={OrderId}", response.Success, input.OrderId);

            return response;
        }
        catch (DaprException ex)
        {
            _logger.LogError(ex,
                "Dapr error reserving inventory. OrderId={OrderId}, ServiceName={ServiceName}, OperationName={OperationName}",
                input.OrderId, "workflow-orchestrator", "ReserveInventory");
            return new ReservationResult(false, ex.Message);
        }
    }
}
