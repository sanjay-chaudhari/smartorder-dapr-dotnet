using Dapr;
using Dapr.Client;

namespace OrderService.Services;

public class ConfigurationService : IConfigurationService
{
    private int _maxOrderQuantity = 100;
    private bool _discountEnabled = false;
    private readonly DaprClient _daprClient;
    private readonly ILogger<ConfigurationService> _logger;

    public ConfigurationService(DaprClient daprClient, ILogger<ConfigurationService> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    public int MaxOrderQuantity => _maxOrderQuantity;
    public bool DiscountEnabled => _discountEnabled;

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        try
        {
            var config = await _daprClient.GetConfiguration(
                "configstore",
                new[] { "max-order-quantity", "discount-enabled" },
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            ApplyConfig(config.Items);

            // Start background subscription for hot-reload (fire and forget with cancellation)
            _ = SubscribeToChangesAsync(cancellationToken);
        }
        catch (DaprException ex)
        {
            _logger.LogWarning(ex,
                "ConfigStore unavailable at startup, using defaults: MaxOrderQuantity={MaxOrderQuantity}, DiscountEnabled={DiscountEnabled}",
                _maxOrderQuantity, _discountEnabled);
        }
    }

    private async Task SubscribeToChangesAsync(CancellationToken cancellationToken)
    {
        try
        {
            var subscribeResponse = await _daprClient.SubscribeConfiguration(
                "configstore",
                new[] { "max-order-quantity", "discount-enabled" },
                cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            await foreach (var items in subscribeResponse.Source.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                foreach (var kvp in items)
                {
                    _logger.LogInformation(
                        "Configuration changed: {ConfigKey}={NewValue}",
                        kvp.Key, kvp.Value.Value);
                }

                ApplyConfig(items);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected on shutdown — no action needed
        }
        catch (DaprException ex)
        {
            _logger.LogWarning(ex, "ConfigStore subscription lost, continuing with last known values");
        }
    }

    private void ApplyConfig(IReadOnlyDictionary<string, ConfigurationItem> items)
    {
        if (items.TryGetValue("max-order-quantity", out var maxQty))
            _maxOrderQuantity = int.TryParse(maxQty.Value, out var v) ? v : 100;

        if (items.TryGetValue("discount-enabled", out var discount))
            _discountEnabled = bool.TryParse(discount.Value, out var b) && b;
    }

    private void ApplyConfig(IDictionary<string, ConfigurationItem> items)
    {
        if (items.TryGetValue("max-order-quantity", out var maxQty))
            _maxOrderQuantity = int.TryParse(maxQty.Value, out var v) ? v : 100;

        if (items.TryGetValue("discount-enabled", out var discount))
            _discountEnabled = bool.TryParse(discount.Value, out var b) && b;
    }
}
