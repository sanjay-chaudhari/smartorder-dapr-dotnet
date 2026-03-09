using Dapr.Client;
using FluentAssertions;
using System.Net.Http.Json;

namespace Integration.Tests;

/// <summary>
/// Integration tests for the Dapr Virtual Actor building block.
/// Requires the full docker-compose stack running (inventory-service + its sidecar).
/// </summary>
[Trait("Category", "RequiresDapr")]
public class ActorIntegrationTests : IntegrationTestBase
{
    private record ReserveRequest(string ProductId, int Quantity, string OrderId);
    private record ReleaseRequest(string ProductId, int Quantity, string OrderId);
    private record ReserveResponse(bool Success, string? FailureReason);
    private record ReleaseResponse(bool Success);
    private record StockResponse(string ProductId, int AvailableQuantity, int ReservedQuantity);

    private static HttpClient CreateInventoryClient()
    {
        var port = Environment.GetEnvironmentVariable("INVENTORY_PORT") ?? "5002";
        return new HttpClient { BaseAddress = new Uri($"http://localhost:{port}") };
    }

    private static async Task SeedActorStock(string productId, int quantity)
    {
        // Seed actor state directly via Dapr state store using the actor key format:
        // {appId}||{actorType}||{actorId}||{stateKey}
        var daprPort = int.Parse(Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "3502");
        var daprClient = new DaprClientBuilder()
            .UseHttpEndpoint($"http://localhost:{daprPort}")
            .Build();

        var key = $"inventory-service||OrderActor||{productId}||inventory-state";
        await daprClient.SaveStateAsync("statestore", key,
            new { ProductId = productId, AvailableQuantity = quantity, ReservedQuantity = 0 })
            .ConfigureAwait(false);
    }

    // -----------------------------------------------------------------------
    // Actor reserve — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ActorReserve_WhenSufficientStock_ShouldReturnSuccess()
    {
        var productId = $"actor-{Guid.NewGuid():N}";
        await SeedActorStock(productId, 20);

        using var http = CreateInventoryClient();
        var response = await http.PostAsJsonAsync("/inventory/actor/reserve",
            new ReserveRequest(productId, 5, $"order-{Guid.NewGuid():N}")).ConfigureAwait(false);

        response.IsSuccessStatusCode.Should().BeTrue();
        var result = await response.Content.ReadFromJsonAsync<ReserveResponse>().ConfigureAwait(false);
        result!.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ActorReserve_WhenInsufficientStock_ShouldReturnFailure()
    {
        var productId = $"actor-{Guid.NewGuid():N}";
        await SeedActorStock(productId, 2);

        using var http = CreateInventoryClient();
        var response = await http.PostAsJsonAsync("/inventory/actor/reserve",
            new ReserveRequest(productId, 100, $"order-{Guid.NewGuid():N}")).ConfigureAwait(false);

        // Returns 422 with success:false
        ((int)response.StatusCode).Should().Be(422);
        var result = await response.Content.ReadFromJsonAsync<ReserveResponse>().ConfigureAwait(false);
        result!.Success.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // Actor get stock
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ActorGetStock_WhenStockExists_ShouldReturnStockLevels()
    {
        var productId = $"actor-{Guid.NewGuid():N}";
        await SeedActorStock(productId, 30);

        using var http = CreateInventoryClient();
        var response = await http.GetAsync($"/inventory/actor/{productId}/stock").ConfigureAwait(false);

        response.IsSuccessStatusCode.Should().BeTrue();
        var stock = await response.Content.ReadFromJsonAsync<StockResponse>().ConfigureAwait(false);
        stock.Should().NotBeNull();
        stock!.ProductId.Should().Be(productId);
        stock.AvailableQuantity.Should().Be(30);
    }

    // -----------------------------------------------------------------------
    // Actor reserve then release — round-trip
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ActorReserveThenRelease_ShouldRestoreStock()
    {
        var productId = $"actor-{Guid.NewGuid():N}";
        var orderId = $"order-{Guid.NewGuid():N}";
        await SeedActorStock(productId, 10);

        using var http = CreateInventoryClient();

        // Reserve 4
        var reserveResp = await http.PostAsJsonAsync("/inventory/actor/reserve",
            new ReserveRequest(productId, 4, orderId)).ConfigureAwait(false);
        reserveResp.IsSuccessStatusCode.Should().BeTrue();

        // Release 4
        var releaseResp = await http.PostAsJsonAsync("/inventory/actor/release",
            new ReleaseRequest(productId, 4, orderId)).ConfigureAwait(false);
        releaseResp.IsSuccessStatusCode.Should().BeTrue();

        // Stock should be back to 10
        var stockResp = await http.GetAsync($"/inventory/actor/{productId}/stock").ConfigureAwait(false);
        var stock = await stockResp.Content.ReadFromJsonAsync<StockResponse>().ConfigureAwait(false);
        stock!.AvailableQuantity.Should().Be(10);
        stock.ReservedQuantity.Should().Be(0);
    }
}
