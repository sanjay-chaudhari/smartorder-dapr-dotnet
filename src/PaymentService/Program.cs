using PaymentService.Endpoints;
using PaymentService.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDaprClient();
builder.Services.AddHealthChecks();
builder.Services.AddScoped<IPaymentService, PaymentService.Services.PaymentService>();

var app = builder.Build();

app.UseCloudEvents();
app.MapSubscribeHandler();

app.MapPaymentEndpoints();
app.MapHealthChecks("/health");

app.Run();
