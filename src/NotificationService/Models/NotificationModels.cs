namespace NotificationService.Models;

public record SendNotificationRequest(string OrderId, string CustomerId, string Message);

public record NotificationSentEvent(string OrderId, string CustomerId, DateTimeOffset SentAt);

public record OrderPlacedEvent(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal Price,
    DateTimeOffset PlacedAt);
