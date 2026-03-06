using System.Diagnostics;
using Dapr.Workflow;
using WorkflowOrchestrator.Components;
using WorkflowOrchestrator.Models;

namespace WorkflowOrchestrator.Services;

public class WorkflowService : IWorkflowService
{
    private readonly DaprWorkflowClient _workflowClient;
    private readonly ILogger<WorkflowService> _logger;

    public WorkflowService(DaprWorkflowClient workflowClient, ILogger<WorkflowService> logger)
    {
        _workflowClient = workflowClient;
        _logger = logger;
    }

    public async Task<StartSagaResponse> StartOrderSagaAsync(
        StartWorkflowRequest request,
        CancellationToken cancellationToken)
    {
        var instanceId = Guid.NewGuid().ToString("N");
        var input = new OrderSagaInput(
            instanceId,
            request.ProductId,
            request.Quantity,
            request.Price,
            request.CustomerId);

        _logger.LogInformation(
            "Starting order saga. InstanceId={InstanceId}, ServiceName={ServiceName}, OperationName={OperationName}, TraceId={TraceId}",
            instanceId, "workflow-orchestrator", "StartOrderSaga",
            Activity.Current?.TraceId.ToString() ?? string.Empty);

        await _workflowClient
            .ScheduleNewWorkflowAsync(
                nameof(OrderSagaWorkflow),
                instanceId,
                input,
                null,
                cancellationToken)
            .ConfigureAwait(false);

        return new StartSagaResponse(instanceId);
    }

    public async Task<WorkflowStatusResponse> GetStatusAsync(
        string instanceId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Getting workflow status. InstanceId={InstanceId}, ServiceName={ServiceName}, OperationName={OperationName}",
            instanceId, "workflow-orchestrator", "GetWorkflowStatus");

        var state = await _workflowClient
            .GetWorkflowStateAsync(instanceId, false, cancellationToken)
            .ConfigureAwait(false);

        if (state is null)
            return new WorkflowStatusResponse(instanceId, WorkflowStatus.Running, null);

        var status = state.RuntimeStatus switch
        {
            WorkflowRuntimeStatus.Completed  => WorkflowStatus.Completed,
            WorkflowRuntimeStatus.Failed     => WorkflowStatus.Failed,
            WorkflowRuntimeStatus.Terminated => WorkflowStatus.Terminated,
            _                                => WorkflowStatus.Running
        };

        string? failureReason = null;
        if (state.RuntimeStatus == WorkflowRuntimeStatus.Failed)
            failureReason = state.FailureDetails?.ErrorMessage ?? "Unknown failure";

        return new WorkflowStatusResponse(instanceId, status, failureReason);
    }
}
