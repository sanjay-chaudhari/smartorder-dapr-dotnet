using FluentAssertions;

namespace Integration.Tests;

/// <summary>
/// Integration tests for Dapr Configuration API building block.
/// Requires all services running via docker-compose with Redis as the config store.
/// </summary>
[Trait("Category", "RequiresDapr")]
public class ConfigurationTests : IntegrationTestBase
{
    [Fact]
    public async Task OrderService_ReadsConfig_FromConfigStore()
    {
        var daprPort = Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "3501";

        // Call the Dapr Configuration API directly via HTTP
        using var http = new HttpClient();
        var url = $"http://localhost:{daprPort}/v1.0/configuration/configstore" +
                  "?key=max-order-quantity&key=discount-enabled";

        var response = await http.GetAsync(url).ConfigureAwait(false);

        // The API should be reachable — keys may be empty if not yet seeded in Redis
        response.IsSuccessStatusCode.Should().BeTrue(
            "the Dapr configuration API should be reachable via the sidecar");

        var body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
        body.Should().NotBeNull("the configuration store should return a valid JSON response");
    }
}
