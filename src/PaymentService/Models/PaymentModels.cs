namespace PaymentService.Models;

public record ProcessPaymentRequest(string OrderId, decimal Amount, string CustomerId);

public record ProcessPaymentResponse(bool Success, string? TransactionId, string? FailureReason);

public record RefundPaymentRequest(string OrderId, string TransactionId, decimal Amount);

public record RefundPaymentResponse(bool Success, string? FailureReason);

public record OrderPlacedEvent(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal Price,
    DateTimeOffset PlacedAt);
