using WorkflowOrchestrator.Models;

namespace WorkflowOrchestrator.Services;

public interface IWorkflowService
{
    Task<StartSagaResponse> StartOrderSagaAsync(StartWorkflowRequest request, CancellationToken cancellationToken);
    Task<WorkflowStatusResponse> GetStatusAsync(string instanceId, CancellationToken cancellationToken);
}
