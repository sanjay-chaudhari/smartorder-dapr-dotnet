using Dapr.Client;
using OrderService.Models;

namespace OrderService.Services;

/// <summary>
/// Production implementation: delegates to DaprClient.InvokeMethodAsync.
/// </summary>
public class DaprInventoryClient : IInventoryClient
{
    private readonly DaprClient _daprClient;

    public DaprInventoryClient(DaprClient daprClient)
    {
        _daprClient = daprClient;
    }

    public Task<ReserveInventoryResponse> ReserveAsync(
        ReserveInventoryRequest request,
        CancellationToken cancellationToken)
    {
        return _daprClient.InvokeMethodAsync<ReserveInventoryRequest, ReserveInventoryResponse>(
            "inventory-service",
            "inventory/reserve",
            request,
            cancellationToken);
    }
}
