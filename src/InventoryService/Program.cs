using InventoryService.Actors;
using InventoryService.Endpoints;
using InventoryService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<IInventoryService, InventoryService.Services.InventoryService>();

// Dapr Virtual Actors
builder.Services.AddActors(options =>
{
    options.Actors.RegisterActor<OrderActor>();
});

var app = builder.Build();

app.MapInventoryEndpoints();
app.MapActorEndpoints();
app.MapActorsHandlers();
app.MapHealthChecks("/health");

app.Run();
