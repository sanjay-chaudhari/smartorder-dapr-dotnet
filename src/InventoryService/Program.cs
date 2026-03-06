using InventoryService.Endpoints;
using InventoryService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<IInventoryService, InventoryService.Services.InventoryService>();

var app = builder.Build();

app.MapInventoryEndpoints();
app.MapHealthChecks("/health");

app.Run();
