using System.Diagnostics;
using Dapr;
using Dapr.Client;
using PaymentService.Models;

namespace PaymentService.Services;

public class PaymentService : IPaymentService
{
    private string? _paymentApiKey;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly DaprClient _daprClient;
    private readonly ILogger<PaymentService> _logger;

    public PaymentService(DaprClient daprClient, ILogger<PaymentService> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    // Kept for interface compatibility — no-op since we lazy-load on first use
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_paymentApiKey is not null) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_paymentApiKey is not null) return;

            var secrets = await _daprClient
                .GetSecretAsync("secretstore", "payment-api-key", cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _paymentApiKey = secrets.TryGetValue("payment-api-key", out var key) ? key : string.Empty;

            _logger.LogInformation(
                "PaymentService initialized. ServiceName={ServiceName}", "payment-service");
        }
        catch (DaprException ex)
        {
            _logger.LogError(ex,
                "Failed to retrieve secret. ServiceName={ServiceName}, SecretName={SecretName}",
                "payment-service", "payment-api-key");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task<ProcessPaymentResponse> ProcessPaymentAsync(
        ProcessPaymentRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "payment-service",
            ["OperationName"] = "ProcessPayment",
            ["OrderId"] = request.OrderId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? string.Empty,
            ["SpanId"] = Activity.Current?.SpanId.ToString() ?? string.Empty
        }))
        {
            try
            {
                _logger.LogInformation(
                    "Processing payment of {Amount} for order {OrderId}", request.Amount, request.OrderId);

                var transactionId = $"txn-{Guid.NewGuid():N}";

                _logger.LogInformation(
                    "Payment processed successfully. TransactionId={TransactionId}, OrderId={OrderId}",
                    transactionId, request.OrderId);

                return new ProcessPaymentResponse(true, transactionId, null);
            }
            catch (DaprException ex)
            {
                _logger.LogError(ex,
                    "Dapr error processing payment for {OrderId}. ServiceName={ServiceName}, OperationName={OperationName}",
                    request.OrderId, "payment-service", "ProcessPayment");
                throw;
            }
        }
    }

    public async Task<RefundPaymentResponse> RefundAsync(
        RefundPaymentRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "payment-service",
            ["OperationName"] = "RefundPayment",
            ["OrderId"] = request.OrderId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? string.Empty,
            ["SpanId"] = Activity.Current?.SpanId.ToString() ?? string.Empty
        }))
        {
            try
            {
                _logger.LogInformation(
                    "Refunding {Amount} for order {OrderId}, transaction {TransactionId}",
                    request.Amount, request.OrderId, request.TransactionId);

                await Task.CompletedTask.ConfigureAwait(false);

                _logger.LogInformation(
                    "Refund processed successfully for order {OrderId}", request.OrderId);

                return new RefundPaymentResponse(true, null);
            }
            catch (DaprException ex)
            {
                _logger.LogError(ex,
                    "Dapr error refunding payment for {OrderId}. ServiceName={ServiceName}, OperationName={OperationName}",
                    request.OrderId, "payment-service", "RefundPayment");
                throw;
            }
        }
    }
}
