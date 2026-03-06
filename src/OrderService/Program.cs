using Dapr.Client;
using FluentValidation;
using OrderService.Endpoints;
using OrderService.Services;

var builder = WebApplication.CreateBuilder(args);

// Dapr
builder.Services.AddDaprClient();

// Health checks
builder.Services.AddHealthChecks();

// Application services
builder.Services.AddOrderServices();

var app = builder.Build();

// Initialize configuration service (load feature flags from Dapr config store)
var configService = app.Services.GetRequiredService<IConfigurationService>();
await configService.InitializeAsync(app.Lifetime.ApplicationStopping);

app.MapOrderEndpoints();
app.MapHealthChecks("/health");

app.Run();
