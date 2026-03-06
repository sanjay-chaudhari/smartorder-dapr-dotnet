using Dapr.Client;
using FluentAssertions;

namespace Integration.Tests;

/// <summary>
/// Integration tests for Dapr secrets management building block.
/// Requires all services running via docker-compose with /components/secrets.json mounted.
/// </summary>
[Trait("Category", "RequiresDapr")]
public class SecretsTests : IntegrationTestBase
{
    [Fact]
    public async Task OrderService_ReadsSecret_FromSecretStore()
    {
        var daprPort = int.Parse(Environment.GetEnvironmentVariable("DAPR_HTTP_PORT") ?? "3501");

        var client = new DaprClientBuilder()
            .UseHttpEndpoint($"http://localhost:{daprPort}")
            .Build();

        // Verify the secret store is accessible and returns the expected key
        var secrets = await client
            .GetSecretAsync("secretstore", "smtp-password")
            .ConfigureAwait(false);

        secrets.Should().NotBeNull();
        secrets.Should().ContainKey("smtp-password",
            "the local file secret store should expose the smtp-password key from secrets.json");
    }

    [Fact]
    public async Task PaymentService_ReadsSecret_FromSecretStore()
    {
        var daprPort = int.Parse(Environment.GetEnvironmentVariable("PAYMENT_DAPR_HTTP_PORT") ?? "3503");

        var client = new DaprClientBuilder()
            .UseHttpEndpoint($"http://localhost:{daprPort}")
            .Build();

        var secrets = await client
            .GetSecretAsync("secretstore", "payment-api-key")
            .ConfigureAwait(false);

        secrets.Should().NotBeNull();
        secrets.Should().ContainKey("payment-api-key",
            "the local file secret store should expose the payment-api-key from secrets.json");
    }
}
