using Dapr.Actors;
using Dapr.Actors.Runtime;
using FluentAssertions;
using InventoryService.Actors;
using InventoryService.Models;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using System.Reflection;

namespace InventoryService.Tests;

/// <summary>
/// Unit tests for OrderActor.
/// ActorHost.CreateForTest does not wire a DaprStateProvider, so we inject a mock
/// IActorStateManager via reflection into the actor's StateManager backing field.
/// This gives us full in-memory state control without a running Dapr sidecar.
/// </summary>
public class OrderActorTests
{
    // -----------------------------------------------------------------------
    // In-memory IActorStateManager — simple dictionary-backed implementation
    // -----------------------------------------------------------------------
    private sealed class InMemoryStateManager : IActorStateManager
    {
        private readonly Dictionary<string, object?> _store = new();

        public Task AddStateAsync<T>(string stateName, T value, CancellationToken cancellationToken = default)
        {
            _store[stateName] = value;
            return Task.CompletedTask;
        }

        public Task AddStateAsync<T>(string stateName, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _store[stateName] = value;
            return Task.CompletedTask;
        }

        public Task<T> GetStateAsync<T>(string stateName, CancellationToken cancellationToken = default)
        {
            if (_store.TryGetValue(stateName, out var v) && v is T typed)
                return Task.FromResult(typed);
            throw new KeyNotFoundException(stateName);
        }

        public Task SetStateAsync<T>(string stateName, T value, CancellationToken cancellationToken = default)
        {
            _store[stateName] = value;
            return Task.CompletedTask;
        }

        public Task SetStateAsync<T>(string stateName, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            _store[stateName] = value;
            return Task.CompletedTask;
        }

        public Task RemoveStateAsync(string stateName, CancellationToken cancellationToken = default)
        {
            _store.Remove(stateName);
            return Task.CompletedTask;
        }

        public Task<bool> TryAddStateAsync<T>(string stateName, T value, CancellationToken cancellationToken = default)
        {
            if (_store.ContainsKey(stateName)) return Task.FromResult(false);
            _store[stateName] = value;
            return Task.FromResult(true);
        }

        public Task<bool> TryAddStateAsync<T>(string stateName, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
        {
            if (_store.ContainsKey(stateName)) return Task.FromResult(false);
            _store[stateName] = value;
            return Task.FromResult(true);
        }

        public Task<ConditionalValue<T>> TryGetStateAsync<T>(string stateName, CancellationToken cancellationToken = default)
        {
            if (_store.TryGetValue(stateName, out var v) && v is T typed)
                return Task.FromResult(new ConditionalValue<T>(true, typed));
            return Task.FromResult(new ConditionalValue<T>(false, default!));
        }

        public Task<bool> TryRemoveStateAsync(string stateName, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_store.Remove(stateName));
        }

        public Task<bool> ContainsStateAsync(string stateName, CancellationToken cancellationToken = default)
            => Task.FromResult(_store.ContainsKey(stateName));

        public Task<T> GetOrAddStateAsync<T>(string stateName, T value, CancellationToken cancellationToken = default)
        {
            if (_store.TryGetValue(stateName, out var existing) && existing is T typed)
                return Task.FromResult(typed);
            _store[stateName] = value;
            return Task.FromResult(value);
        }

        public Task<T> GetOrAddStateAsync<T>(string stateName, T value, TimeSpan ttl, CancellationToken cancellationToken = default)
            => GetOrAddStateAsync(stateName, value, cancellationToken);

        public Task<T> AddOrUpdateStateAsync<T>(string stateName, T addValue, Func<string, T, T> updateValueFactory, CancellationToken cancellationToken = default)
        {
            T result;
            if (_store.TryGetValue(stateName, out var existing) && existing is T typed)
                result = updateValueFactory(stateName, typed);
            else
                result = addValue;
            _store[stateName] = result;
            return Task.FromResult(result);
        }

        public Task<T> AddOrUpdateStateAsync<T>(string stateName, T addValue, Func<string, T, T> updateValueFactory, TimeSpan ttl, CancellationToken cancellationToken = default)
            => AddOrUpdateStateAsync(stateName, addValue, updateValueFactory, cancellationToken);

        public Task<IEnumerable<string>> GetStateNamesAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IEnumerable<string>>(_store.Keys.ToList());

        public Task ClearCacheAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task SaveStateAsync(CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    // -----------------------------------------------------------------------
    // Factory — creates actor with injected in-memory state manager
    // -----------------------------------------------------------------------
    private static OrderActor CreateActor(string productId = "prod-test")
    {
        var host = ActorHost.CreateForTest<OrderActor>(
            new ActorTestOptions { ActorId = new ActorId(productId) });
        var actor = new OrderActor(host, NullLogger<OrderActor>.Instance);

        // Inject in-memory state manager via reflection (StateManager is set by Actor base ctor
        // from the host's state provider; we replace it with our in-memory implementation).
        var field = typeof(Actor).GetField("_stateManager",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? typeof(Actor).GetField("stateManager",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (field is not null)
        {
            field.SetValue(actor, new InMemoryStateManager());
        }
        else
        {
            // Fallback: find the backing field by type
            var stateManagerField = typeof(Actor)
                .GetFields(BindingFlags.NonPublic | BindingFlags.Instance)
                .FirstOrDefault(f => f.FieldType == typeof(IActorStateManager));
            stateManagerField?.SetValue(actor, new InMemoryStateManager());
        }

        return actor;
    }

    private static async Task SeedActorStock(OrderActor actor, string productId, int quantity)
    {
        await actor.StateManager.SetStateAsync(
            "inventory-state",
            new InventoryItem(productId, quantity, 0),
            CancellationToken.None);
        await actor.StateManager.SaveStateAsync(CancellationToken.None);
    }

    // -----------------------------------------------------------------------
    // ReserveAsync — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReserveAsync_WhenSufficientStock_ShouldReturnSuccess()
    {
        var actor = CreateActor("prod-1");
        await SeedActorStock(actor, "prod-1", 20);

        var result = await actor.ReserveAsync(
            new ReserveInventoryRequest("prod-1", 5, "order-001"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ReserveAsync_WhenSufficientStock_ShouldDecrementAvailableAndIncrementReserved()
    {
        var actor = CreateActor("prod-2");
        await SeedActorStock(actor, "prod-2", 20);

        await actor.ReserveAsync(new ReserveInventoryRequest("prod-2", 7, "order-002"), CancellationToken.None);

        var stock = await actor.GetStockAsync(CancellationToken.None);
        stock.Should().NotBeNull();
        stock!.AvailableQuantity.Should().Be(13);
        stock.ReservedQuantity.Should().Be(7);
    }

    [Fact]
    public async Task ReserveAsync_WhenInsufficientStock_ShouldReturnFailure()
    {
        var actor = CreateActor("prod-3");
        await SeedActorStock(actor, "prod-3", 3);

        var result = await actor.ReserveAsync(
            new ReserveInventoryRequest("prod-3", 10, "order-003"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("Insufficient");
    }

    [Fact]
    public async Task ReserveAsync_WhenInsufficientStock_ShouldNotChangeStock()
    {
        var actor = CreateActor("prod-4");
        await SeedActorStock(actor, "prod-4", 3);

        await actor.ReserveAsync(new ReserveInventoryRequest("prod-4", 10, "order-004"), CancellationToken.None);

        var stock = await actor.GetStockAsync(CancellationToken.None);
        stock!.AvailableQuantity.Should().Be(3);
        stock.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public async Task ReserveAsync_WhenExactStock_ShouldSucceedAndLeaveZeroAvailable()
    {
        var actor = CreateActor("prod-5");
        await SeedActorStock(actor, "prod-5", 5);

        var result = await actor.ReserveAsync(
            new ReserveInventoryRequest("prod-5", 5, "order-005"), CancellationToken.None);

        result.Success.Should().BeTrue();
        var stock = await actor.GetStockAsync(CancellationToken.None);
        stock!.AvailableQuantity.Should().Be(0);
        stock.ReservedQuantity.Should().Be(5);
    }

    // -----------------------------------------------------------------------
    // ReleaseAsync — happy path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReleaseAsync_WhenStockExists_ShouldReturnSuccess()
    {
        var actor = CreateActor("prod-6");
        await SeedActorStock(actor, "prod-6", 10);
        await actor.ReserveAsync(new ReserveInventoryRequest("prod-6", 4, "order-006"), CancellationToken.None);

        var result = await actor.ReleaseAsync(
            new ReleaseInventoryRequest("prod-6", 4, "order-006"), CancellationToken.None);

        result.Success.Should().BeTrue();
    }

    [Fact]
    public async Task ReleaseAsync_WhenStockExists_ShouldRestoreAvailableAndDecrementReserved()
    {
        var actor = CreateActor("prod-7");
        await SeedActorStock(actor, "prod-7", 10);
        await actor.ReserveAsync(new ReserveInventoryRequest("prod-7", 4, "order-007"), CancellationToken.None);

        await actor.ReleaseAsync(new ReleaseInventoryRequest("prod-7", 4, "order-007"), CancellationToken.None);

        var stock = await actor.GetStockAsync(CancellationToken.None);
        stock!.AvailableQuantity.Should().Be(10);
        stock.ReservedQuantity.Should().Be(0);
    }

    [Fact]
    public async Task ReleaseAsync_WhenNoStateExists_ShouldReturnFailure()
    {
        var actor = CreateActor("prod-8");

        var result = await actor.ReleaseAsync(
            new ReleaseInventoryRequest("prod-8", 1, "order-008"), CancellationToken.None);

        result.Success.Should().BeFalse();
    }

    // -----------------------------------------------------------------------
    // GetStockAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStockAsync_WhenNoState_ShouldReturnNull()
    {
        var actor = CreateActor("prod-9");
        var stock = await actor.GetStockAsync(CancellationToken.None);
        stock.Should().BeNull();
    }

    [Fact]
    public async Task GetStockAsync_AfterSeed_ShouldReturnCorrectStock()
    {
        var actor = CreateActor("prod-10");
        await SeedActorStock(actor, "prod-10", 15);

        var stock = await actor.GetStockAsync(CancellationToken.None);
        stock.Should().NotBeNull();
        stock!.AvailableQuantity.Should().Be(15);
        stock.ProductId.Should().Be("prod-10");
    }

    // -----------------------------------------------------------------------
    // Sequential operations
    // -----------------------------------------------------------------------

    [Fact]
    public async Task MultipleReserves_ShouldAccumulateCorrectly()
    {
        var actor = CreateActor("prod-11");
        await SeedActorStock(actor, "prod-11", 20);

        await actor.ReserveAsync(new ReserveInventoryRequest("prod-11", 3, "order-a"), CancellationToken.None);
        await actor.ReserveAsync(new ReserveInventoryRequest("prod-11", 5, "order-b"), CancellationToken.None);

        var stock = await actor.GetStockAsync(CancellationToken.None);
        stock!.AvailableQuantity.Should().Be(12);
        stock.ReservedQuantity.Should().Be(8);
    }
}
