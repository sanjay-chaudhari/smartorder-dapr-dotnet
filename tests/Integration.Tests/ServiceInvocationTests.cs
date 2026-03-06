using Dapr.Client;
using FluentAssertions;

namespace Integration.Tests;

/// <summary>
/// Integration tests for Dapr service invocation building block.
/// Requires all services running via docker-compose.
/// </summary>
[Trait("Category", "RequiresDapr")]
public class ServiceInvocationTests : IntegrationTestBase
{
    private record ReserveInventoryRequest(string ProductId, int Quantity, string OrderId);
    private record ReserveInventoryResponse(bool Success, string? FailureReason);

    [Fact]
    public async Task OrderService_InvokesInventoryService_ReturnsValidResponse()
    {
        // Seed inventory state first, then invoke reserve via order-service's Dapr sidecar
        var daprPort = int.Parse(Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "3501");

        var client = new DaprClientBuilder()
            .UseHttpEndpoint($"http://localhost:{daprPort}")
            .Build();

        // Seed inventory item so reservation can succeed
        var productId = $"prod-{Guid.NewGuid():N}";
        var inventoryKey = $"inventory-{productId}";
        var inventoryItem = new { ProductId = productId, AvailableQuantity = 10, ReservedQuantity = 0 };
        await client.SaveStateAsync("statestore", inventoryKey, inventoryItem).ConfigureAwait(false);

        var request = new ReserveInventoryRequest(productId, 2, $"order-{Guid.NewGuid():N}");

        var response = await client.InvokeMethodAsync<ReserveInventoryRequest, ReserveInventoryResponse>(
            "inventory-service",
            "inventory/reserve",
            request,
            CancellationToken.None).ConfigureAwait(false);

        response.Should().NotBeNull();
        response.Success.Should().BeTrue();

        // Cleanup
        await client.DeleteStateAsync("statestore", inventoryKey).ConfigureAwait(false);
    }
}
