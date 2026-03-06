using Dapr.Workflow;
using WorkflowOrchestrator.Models;

namespace WorkflowOrchestrator.Components;

public class ValidateOrderActivity : WorkflowActivity<ValidateOrderInput, ValidationResult>
{
    private readonly ILogger<ValidateOrderActivity> _logger;

    public ValidateOrderActivity(ILogger<ValidateOrderActivity> logger)
    {
        _logger = logger;
    }

    public override Task<ValidationResult> RunAsync(WorkflowActivityContext context, ValidateOrderInput input)
    {
        _logger.LogInformation(
            "Validating order. OrderId={OrderId}, OperationName={OperationName}, ServiceName={ServiceName}",
            input.OrderId, "ValidateOrder", "workflow-orchestrator");

        if (input.Quantity <= 0)
        {
            _logger.LogWarning("Validation failed: Quantity <= 0. OrderId={OrderId}", input.OrderId);
            return Task.FromResult(new ValidationResult(false, "Quantity must be greater than zero"));
        }

        if (input.Price <= 0)
        {
            _logger.LogWarning("Validation failed: Price <= 0. OrderId={OrderId}", input.OrderId);
            return Task.FromResult(new ValidationResult(false, "Price must be greater than zero"));
        }

        _logger.LogInformation("Order validated successfully. OrderId={OrderId}", input.OrderId);
        return Task.FromResult(new ValidationResult(true, null));
    }
}
