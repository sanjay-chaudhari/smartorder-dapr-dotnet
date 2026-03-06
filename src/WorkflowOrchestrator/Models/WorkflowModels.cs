namespace WorkflowOrchestrator.Models;

public enum WorkflowStatus { Running, Completed, Failed, Terminated }

// Saga top-level I/O
public record OrderSagaInput(
    string OrderId,
    string ProductId,
    int Quantity,
    decimal Price,
    string CustomerId);

public record OrderSagaResult(bool Success, string? FailureReason);

// Workflow API request/response
public record StartWorkflowRequest(
    string ProductId,
    int Quantity,
    decimal Price,
    string CustomerId);

public record StartSagaResponse(string InstanceId);

public record WorkflowStatusResponse(
    string InstanceId,
    WorkflowStatus Status,
    string? FailureReason);

// ValidateOrder activity
public record ValidateOrderInput(string OrderId, int Quantity, decimal Price);
public record ValidationResult(bool IsValid, string? FailureReason);

// ReserveInventory activity
public record ReserveInventoryInput(string OrderId, string ProductId, int Quantity);
public record ReservationResult(bool Success, string? FailureReason);

// ProcessPayment activity
public record ProcessPaymentInput(string OrderId, decimal Amount, string CustomerId);
public record PaymentResult(bool Success, string? TransactionId, string? FailureReason);

// SendNotification activity
public record SendNotificationInput(string OrderId, string CustomerId, string Message);
public record NotificationResult(bool Success);

// Compensation activities
public record ReleaseInventoryInput(string OrderId, string ProductId, int Quantity);
public record RefundPaymentInput(string OrderId, string TransactionId, decimal Amount);
