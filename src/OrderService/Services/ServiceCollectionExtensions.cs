using OrderService.Services;

namespace Microsoft.Extensions.DependencyInjection;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrderServices(this IServiceCollection services)
    {
        services.AddScoped<IOrderService, OrderService.Services.OrderService>();
        services.AddScoped<IOrderStateService, OrderStateService>();
        services.AddScoped<IInventoryClient, DaprInventoryClient>();
        services.AddSingleton<IConfigurationService, ConfigurationService>();
        services.AddSingleton<OrderValidator>();
        return services;
    }
}
