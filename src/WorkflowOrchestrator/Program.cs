using Dapr.Workflow;
using WorkflowOrchestrator.Components;
using WorkflowOrchestrator.Endpoints;
using WorkflowOrchestrator.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddHealthChecks();

builder.Services.AddDaprWorkflow(options =>
{
    options.RegisterWorkflow<OrderSagaWorkflow>();
    options.RegisterActivity<ValidateOrderActivity>();
    options.RegisterActivity<ReserveInventoryActivity>();
    options.RegisterActivity<ProcessPaymentActivity>();
    options.RegisterActivity<SendNotificationActivity>();
    options.RegisterActivity<ReleaseInventoryReservationActivity>();
    options.RegisterActivity<RefundPaymentActivity>();
});

builder.Services.AddScoped<IWorkflowService, WorkflowService>();

var app = builder.Build();

app.MapWorkflowEndpoints();
app.MapHealthChecks("/health");

app.Run();
