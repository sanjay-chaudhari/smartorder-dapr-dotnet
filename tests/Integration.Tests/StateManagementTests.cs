using Dapr.Client;
using FluentAssertions;

namespace Integration.Tests;

/// <summary>
/// Integration tests for Dapr state management building block.
/// Requires a running Dapr sidecar — run via docker-compose in CI.
/// </summary>
[Trait("Category", "RequiresDapr")]
public class StateManagementTests : IntegrationTestBase
{
    private record Order(
        string OrderId,
        string ProductId,
        int Quantity,
        decimal Price,
        string Status,
        DateTimeOffset CreatedAt);

    [Fact]
    public async Task Order_SavedAndRead_ReturnsEquivalentObject()
    {
        // Property 9: State round-trip preserves order data
        // Connects to the Dapr sidecar running alongside order-service in docker-compose
        var daprPort = int.Parse(Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "3501");

        var client = new DaprClientBuilder()
            .UseHttpEndpoint($"http://localhost:{daprPort}")
            .Build();

        var orderId = Guid.NewGuid().ToString("N");
        var key = $"order-{orderId}";
        var original = new Order(orderId, "prod-abc", 3, 29.99m, "Pending", DateTimeOffset.UtcNow);

        await client.SaveStateAsync("statestore", key, original).ConfigureAwait(false);

        var retrieved = await client.GetStateAsync<Order>("statestore", key).ConfigureAwait(false);

        retrieved.Should().NotBeNull();
        retrieved!.OrderId.Should().Be(original.OrderId);
        retrieved.ProductId.Should().Be(original.ProductId);
        retrieved.Quantity.Should().Be(original.Quantity);
        retrieved.Price.Should().Be(original.Price);
        retrieved.Status.Should().Be(original.Status);

        // Cleanup
        await client.DeleteStateAsync("statestore", key).ConfigureAwait(false);
    }
}
