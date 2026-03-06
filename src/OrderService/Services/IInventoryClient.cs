using OrderService.Models;

namespace OrderService.Services;

/// <summary>
/// Abstracts the Dapr service-invocation call to inventory-service,
/// enabling NSubstitute mocking in unit tests without requiring a real DaprClient.
/// </summary>
public interface IInventoryClient
{
    Task<ReserveInventoryResponse> ReserveAsync(
        ReserveInventoryRequest request,
        CancellationToken cancellationToken);
}
