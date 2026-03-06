using Testcontainers.Redis;

namespace Integration.Tests;

/// <summary>
/// Base class for all integration tests.
/// Starts a Redis container before tests and tears it down after.
/// Tests that require a live Dapr sidecar are marked with [Trait("Category", "RequiresDapr")]
/// and should be run via docker-compose in CI.
/// </summary>
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected RedisContainer RedisContainer { get; } = new RedisBuilder()
        .WithImage("redis:7-alpine")
        .Build();

    public async Task InitializeAsync()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try
        {
            await RedisContainer.StartAsync(cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw new InvalidOperationException(
                "Redis container failed to start within 60 seconds. " +
                "Ensure Docker is running and the redis:7-alpine image is available.");
        }
    }

    public async Task DisposeAsync()
    {
        await RedisContainer.DisposeAsync().ConfigureAwait(false);
    }

    protected string GetRedisConnectionString() => RedisContainer.GetConnectionString();
}
