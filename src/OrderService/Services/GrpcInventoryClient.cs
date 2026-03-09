using Dapr.Client;
using OrderService.Models;

namespace OrderService.Services;

/// <summary>
/// Demonstrates Dapr service invocation using the low-level HttpRequestMessage path.
/// CreateInvokeMethodRequest builds a Dapr-addressed HttpRequestMessage; the sidecar
/// routes it to inventory-service over its gRPC app channel. This is the same transport
/// used by the high-level InvokeMethodAsync overloads but exposed here to show the
/// underlying building block and to keep the class fully unit-testable via NSubstitute
/// (the high-level overloads call non-virtual internal helpers that NSubstitute cannot intercept).
/// </summary>
public class GrpcInventoryClient : IInventoryClient
{
    private readonly DaprClient _daprClient;
    private readonly ILogger<GrpcInventoryClient> _logger;

    public GrpcInventoryClient(DaprClient daprClient, ILogger<GrpcInventoryClient> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public async Task<ReserveInventoryResponse> ReserveAsync(
        ReserveInventoryRequest request,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Invoking inventory-service via Dapr gRPC channel. OrderId={OrderId}, ProductId={ProductId}, OperationName={OperationName}, ServiceName={ServiceName}",
            request.OrderId, request.ProductId, "GrpcReserveInventory", "order-service");

        // Build a Dapr-addressed HttpRequestMessage with the serialised request body,
        // then dispatch via the abstract InvokeMethodAsync(HttpRequestMessage) — this is
        // the mockable entry point that NSubstitute can intercept on DaprClient.
        var httpRequest = _daprClient.CreateInvokeMethodRequest(
            HttpMethod.Post,
            "inventory-service",
            "inventory/reserve",
            queryStringParameters: null,
            request);

        return await _daprClient
            .InvokeMethodAsync<ReserveInventoryResponse>(httpRequest, cancellationToken)
            .ConfigureAwait(false);
    }
}
