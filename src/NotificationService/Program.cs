using NotificationService.Endpoints;
using NotificationService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<INotificationService, NotificationService.Services.NotificationService>();

var app = builder.Build();

app.UseCloudEvents();
app.MapSubscribeHandler();

app.MapNotificationEndpoints();
app.MapHealthChecks("/health");

app.Run();
