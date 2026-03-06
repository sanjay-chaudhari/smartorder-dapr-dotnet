namespace OrderService.Services;

public interface IConfigurationService
{
    int MaxOrderQuantity { get; }
    bool DiscountEnabled { get; }
    Task InitializeAsync(CancellationToken cancellationToken);
}
