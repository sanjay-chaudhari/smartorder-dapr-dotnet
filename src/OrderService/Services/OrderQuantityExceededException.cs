namespace OrderService.Services;

public class OrderQuantityExceededException : Exception
{
    public int RequestedQuantity { get; }
    public int MaxQuantity { get; }

    public OrderQuantityExceededException(int requested, int max)
        : base($"Requested quantity {requested} exceeds maximum allowed {max}")
    {
        RequestedQuantity = requested;
        MaxQuantity = max;
    }
}
