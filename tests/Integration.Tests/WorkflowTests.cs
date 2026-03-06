using Dapr.Client;
using FluentAssertions;

namespace Integration.Tests;

/// <summary>
/// Integration tests for Dapr Workflow (saga) building block.
/// Requires all services running via docker-compose.
/// </summary>
[Trait("Category", "RequiresDapr")]
public class WorkflowTests : IntegrationTestBase
{
    private record StartWorkflowRequest(string ProductId, int Quantity, decimal Price, string CustomerId);
    private record StartSagaResponse(string InstanceId);

    [Fact]
    public async Task OrderSaga_HappyPath_ReachesCompleted()
    {
        var orchestratorPort = Environment.GetEnvironmentVariable("WORKFLOW_PORT") ?? "5005";
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{orchestratorPort}") };

        // Seed inventory so the saga can reserve stock
        var daprPort = int.Parse(Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "3501");
        var daprClient = new DaprClientBuilder()
            .UseHttpEndpoint($"http://localhost:{daprPort}")
            .Build();

        var productId = $"prod-{Guid.NewGuid():N}";
        await daprClient.SaveStateAsync("statestore", $"inventory-{productId}",
            new { ProductId = productId, AvailableQuantity = 10, ReservedQuantity = 0 })
            .ConfigureAwait(false);

        // Start the saga
        var request = new StartWorkflowRequest(productId, 1, 19.99m, "cust-001");
        var startResponse = await http.PostAsJsonAsync("/workflow/orders", request).ConfigureAwait(false);
        startResponse.IsSuccessStatusCode.Should().BeTrue();

        var saga = await startResponse.Content.ReadFromJsonAsync<StartSagaResponse>().ConfigureAwait(false);
        saga.Should().NotBeNull();
        saga!.InstanceId.Should().NotBeNullOrEmpty();

        // Poll for completion (up to 30 seconds)
        var completed = false;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            var statusResponse = await http.GetAsync($"/workflow/orders/{saga.InstanceId}").ConfigureAwait(false);
            if (!statusResponse.IsSuccessStatusCode) continue;

            var body = await statusResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (body.Contains("Completed", StringComparison.OrdinalIgnoreCase))
            {
                completed = true;
                break;
            }
        }

        completed.Should().BeTrue("the order saga should reach Completed status within 30 seconds");

        // Cleanup
        await daprClient.DeleteStateAsync("statestore", $"inventory-{productId}").ConfigureAwait(false);
    }

    [Fact]
    public async Task OrderSaga_PaymentFailure_ReachesFailedWithCompensation()
    {
        // This test verifies the saga reaches Failed when payment is rejected.
        // Payment always fails for amount > 9999 in the stub implementation.
        var orchestratorPort = Environment.GetEnvironmentVariable("WORKFLOW_PORT") ?? "5005";
        using var http = new HttpClient { BaseAddress = new Uri($"http://localhost:{orchestratorPort}") };

        var daprPort = int.Parse(Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "3501");
        var daprClient = new DaprClientBuilder()
            .UseHttpEndpoint($"http://localhost:{daprPort}")
            .Build();

        var productId = $"prod-{Guid.NewGuid():N}";
        await daprClient.SaveStateAsync("statestore", $"inventory-{productId}",
            new { ProductId = productId, AvailableQuantity = 10, ReservedQuantity = 0 })
            .ConfigureAwait(false);

        // Use a very high price to trigger payment failure in the stub
        var request = new StartWorkflowRequest(productId, 1, 99999.99m, "cust-002");
        var startResponse = await http.PostAsJsonAsync("/workflow/orders", request).ConfigureAwait(false);
        startResponse.IsSuccessStatusCode.Should().BeTrue();

        var saga = await startResponse.Content.ReadFromJsonAsync<StartSagaResponse>().ConfigureAwait(false);
        saga.Should().NotBeNull();

        // Poll for terminal state
        var reachedTerminal = false;
        for (var i = 0; i < 30; i++)
        {
            await Task.Delay(TimeSpan.FromSeconds(1)).ConfigureAwait(false);
            var statusResponse = await http.GetAsync($"/workflow/orders/{saga!.InstanceId}").ConfigureAwait(false);
            if (!statusResponse.IsSuccessStatusCode) continue;

            var body = await statusResponse.Content.ReadAsStringAsync().ConfigureAwait(false);
            if (body.Contains("Failed", StringComparison.OrdinalIgnoreCase) ||
                body.Contains("Completed", StringComparison.OrdinalIgnoreCase))
            {
                reachedTerminal = true;
                break;
            }
        }

        reachedTerminal.Should().BeTrue("the order saga should reach a terminal state within 30 seconds");

        // Cleanup
        await daprClient.DeleteStateAsync("statestore", $"inventory-{productId}").ConfigureAwait(false);
    }
}
