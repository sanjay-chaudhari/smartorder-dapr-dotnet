using Dapr;
using WorkflowOrchestrator.Models;
using WorkflowOrchestrator.Services;

namespace WorkflowOrchestrator.Endpoints;

public static class WorkflowEndpoints
{
    public static void MapWorkflowEndpoints(this WebApplication app)
    {
        app.MapPost("/workflow/orders", async (
            StartWorkflowRequest request,
            IWorkflowService workflowService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("WorkflowEndpoints");
            try
            {
                var response = await workflowService
                    .StartOrderSagaAsync(request, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Accepted($"/workflow/orders/{response.InstanceId}", response);
            }
            catch (DaprException ex)
            {
                logger.LogError(ex, "Dapr error starting order saga");
                return Results.Problem(statusCode: 503, title: "Workflow service unavailable");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error starting order saga");
                return Results.Problem();
            }
        });

        app.MapGet("/workflow/orders/{instanceId}", async (
            string instanceId,
            IWorkflowService workflowService,
            ILoggerFactory loggerFactory,
            CancellationToken cancellationToken) =>
        {
            var logger = loggerFactory.CreateLogger("WorkflowEndpoints");
            try
            {
                var status = await workflowService
                    .GetStatusAsync(instanceId, cancellationToken)
                    .ConfigureAwait(false);

                return Results.Ok(status);
            }
            catch (DaprException ex)
            {
                logger.LogError(ex, "Dapr error getting workflow status for {InstanceId}", instanceId);
                return Results.Problem(statusCode: 503, title: "Workflow service unavailable");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Unhandled error getting workflow status for {InstanceId}", instanceId);
                return Results.Problem();
            }
        });

        app.MapGet("/health", () => Results.Ok(new { status = "healthy", service = "workflow-orchestrator" }));
    }
}
