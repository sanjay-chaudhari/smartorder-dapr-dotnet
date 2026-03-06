using Dapr.Workflow;
using WorkflowOrchestrator.Models;

namespace WorkflowOrchestrator.Components;

public class OrderSagaWorkflow : Workflow<OrderSagaInput, OrderSagaResult>
{
    public override async Task<OrderSagaResult> RunAsync(WorkflowContext context, OrderSagaInput input)
    {
        // Step 1: Validate order
        var validation = await context.CallActivityAsync<ValidationResult>(
            nameof(ValidateOrderActivity),
            new ValidateOrderInput(input.OrderId, input.Quantity, input.Price));

        if (!validation.IsValid)
            return new OrderSagaResult(false, validation.FailureReason);

        // Step 2: Reserve inventory
        var reservation = await context.CallActivityAsync<ReservationResult>(
            nameof(ReserveInventoryActivity),
            new ReserveInventoryInput(input.OrderId, input.ProductId, input.Quantity));

        if (!reservation.Success)
            return new OrderSagaResult(false, reservation.FailureReason);

        // Step 3: Process payment
        var payment = await context.CallActivityAsync<PaymentResult>(
            nameof(ProcessPaymentActivity),
            new ProcessPaymentInput(input.OrderId, input.Price * input.Quantity, input.CustomerId));

        if (!payment.Success)
        {
            // Compensate: release inventory
            await context.CallActivityAsync<bool>(
                nameof(ReleaseInventoryReservationActivity),
                new ReleaseInventoryInput(input.OrderId, input.ProductId, input.Quantity));

            // Compensate: refund if a transaction was created
            if (payment.TransactionId is not null)
            {
                await context.CallActivityAsync<bool>(
                    nameof(RefundPaymentActivity),
                    new RefundPaymentInput(input.OrderId, payment.TransactionId, input.Price * input.Quantity));
            }

            return new OrderSagaResult(false, payment.FailureReason);
        }

        // Step 4: Send notification (non-fatal)
        await context.CallActivityAsync<NotificationResult>(
            nameof(SendNotificationActivity),
            new SendNotificationInput(
                input.OrderId,
                input.CustomerId,
                $"Your order {input.OrderId} has been confirmed."));

        return new OrderSagaResult(true, null);
    }
}
