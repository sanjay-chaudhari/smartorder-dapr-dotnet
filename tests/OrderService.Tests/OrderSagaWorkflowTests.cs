using Dapr.Workflow;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using NSubstitute;
using WorkflowOrchestrator.Components;
using WorkflowOrchestrator.Models;

namespace OrderService.Tests;

/// <summary>
/// Tests for OrderSagaWorkflow saga logic.
/// WorkflowContext is abstract — mocked via NSubstitute.
/// </summary>
public class OrderSagaWorkflowTests
{
    private static OrderSagaInput ValidInput() =>
        new("order-123", "prod-abc", 2, 49.99m, "cust-xyz");

    // ── Happy path ──────────────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WhenAllActivitiesSucceed_ShouldReturnSuccess()
    {
        // Feature: smart-order, Property 12: Successful saga executes all four activities in order and reaches Completed
        var ctx = Substitute.For<WorkflowContext>();
        var input = ValidInput();

        ctx.CallActivityAsync<ValidationResult>(nameof(ValidateOrderActivity), Arg.Any<object>())
           .Returns(new ValidationResult(true, null));
        ctx.CallActivityAsync<ReservationResult>(nameof(ReserveInventoryActivity), Arg.Any<object>())
           .Returns(new ReservationResult(true, null));
        ctx.CallActivityAsync<PaymentResult>(nameof(ProcessPaymentActivity), Arg.Any<object>())
           .Returns(new PaymentResult(true, "txn-001", null));
        ctx.CallActivityAsync<NotificationResult>(nameof(SendNotificationActivity), Arg.Any<object>())
           .Returns(new NotificationResult(true));

        var workflow = new OrderSagaWorkflow();
        var result = await workflow.RunAsync(ctx, input);

        result.Success.Should().BeTrue();
        result.FailureReason.Should().BeNull();

        // All four activities called in order
        Received.InOrder(() =>
        {
            ctx.CallActivityAsync<ValidationResult>(nameof(ValidateOrderActivity), Arg.Any<object>());
            ctx.CallActivityAsync<ReservationResult>(nameof(ReserveInventoryActivity), Arg.Any<object>());
            ctx.CallActivityAsync<PaymentResult>(nameof(ProcessPaymentActivity), Arg.Any<object>());
            ctx.CallActivityAsync<NotificationResult>(nameof(SendNotificationActivity), Arg.Any<object>());
        });
    }

    // ── Validation failure ───────────────────────────────────────────────────

    [Fact]
    public async Task RunAsync_WhenValidationFails_ShouldReturnFailureWithoutCallingOtherActivities()
    {
        var ctx = Substitute.For<WorkflowContext>();
        var input = ValidInput();

        ctx.CallActivityAsync<ValidationResult>(nameof(ValidateOrderActivity), Arg.Any<object>())
           .Returns(new ValidationResult(false, "Invalid quantity"));

        var workflow = new OrderSagaWorkflow();
        var result = await workflow.RunAsync(ctx, input);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("Invalid quantity");

        await ctx.DidNotReceive()
            .CallActivityAsync<ReservationResult>(nameof(ReserveInventoryActivity), Arg.Any<object>());
        await ctx.DidNotReceive()
            .CallActivityAsync<PaymentResult>(nameof(ProcessPaymentActivity), Arg.Any<object>());
    }

    // ── Inventory failure short-circuit ─────────────────────────────────────

    [Fact]
    public async Task RunAsync_WhenInventoryFails_ShouldSkipPaymentAndReturnFailure()
    {
        // Feature: smart-order, Property 14: Inventory failure skips payment and reaches Failed status
        var ctx = Substitute.For<WorkflowContext>();
        var input = ValidInput();

        ctx.CallActivityAsync<ValidationResult>(nameof(ValidateOrderActivity), Arg.Any<object>())
           .Returns(new ValidationResult(true, null));
        ctx.CallActivityAsync<ReservationResult>(nameof(ReserveInventoryActivity), Arg.Any<object>())
           .Returns(new ReservationResult(false, "Out of stock"));

        var workflow = new OrderSagaWorkflow();
        var result = await workflow.RunAsync(ctx, input);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("Out of stock");

        await ctx.DidNotReceive()
            .CallActivityAsync<PaymentResult>(nameof(ProcessPaymentActivity), Arg.Any<object>());
        await ctx.DidNotReceive()
            .CallActivityAsync<bool>(nameof(ReleaseInventoryReservationActivity), Arg.Any<object>());
    }

    // ── Payment failure with compensation ───────────────────────────────────

    [Fact]
    public async Task RunAsync_WhenPaymentFailsWithNoTransaction_ShouldReleaseInventoryAndNotRefund()
    {
        // Feature: smart-order, Property 13: Payment failure triggers compensation and reaches Failed status
        var ctx = Substitute.For<WorkflowContext>();
        var input = ValidInput();

        ctx.CallActivityAsync<ValidationResult>(nameof(ValidateOrderActivity), Arg.Any<object>())
           .Returns(new ValidationResult(true, null));
        ctx.CallActivityAsync<ReservationResult>(nameof(ReserveInventoryActivity), Arg.Any<object>())
           .Returns(new ReservationResult(true, null));
        ctx.CallActivityAsync<PaymentResult>(nameof(ProcessPaymentActivity), Arg.Any<object>())
           .Returns(new PaymentResult(false, null, "Insufficient funds"));
        ctx.CallActivityAsync<bool>(nameof(ReleaseInventoryReservationActivity), Arg.Any<object>())
           .Returns(true);

        var workflow = new OrderSagaWorkflow();
        var result = await workflow.RunAsync(ctx, input);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("Insufficient funds");

        await ctx.Received(1)
            .CallActivityAsync<bool>(nameof(ReleaseInventoryReservationActivity), Arg.Any<object>());
        await ctx.DidNotReceive()
            .CallActivityAsync<bool>(nameof(RefundPaymentActivity), Arg.Any<object>());
    }

    [Fact]
    public async Task RunAsync_WhenPaymentFailsWithTransaction_ShouldReleaseInventoryAndRefund()
    {
        // Feature: smart-order, Property 13: Payment failure with TransactionId also calls RefundPayment
        var ctx = Substitute.For<WorkflowContext>();
        var input = ValidInput();

        ctx.CallActivityAsync<ValidationResult>(nameof(ValidateOrderActivity), Arg.Any<object>())
           .Returns(new ValidationResult(true, null));
        ctx.CallActivityAsync<ReservationResult>(nameof(ReserveInventoryActivity), Arg.Any<object>())
           .Returns(new ReservationResult(true, null));
        ctx.CallActivityAsync<PaymentResult>(nameof(ProcessPaymentActivity), Arg.Any<object>())
           .Returns(new PaymentResult(false, "txn-partial", "Payment gateway error"));
        ctx.CallActivityAsync<bool>(nameof(ReleaseInventoryReservationActivity), Arg.Any<object>())
           .Returns(true);
        ctx.CallActivityAsync<bool>(nameof(RefundPaymentActivity), Arg.Any<object>())
           .Returns(true);

        var workflow = new OrderSagaWorkflow();
        var result = await workflow.RunAsync(ctx, input);

        result.Success.Should().BeFalse();

        await ctx.Received(1)
            .CallActivityAsync<bool>(nameof(ReleaseInventoryReservationActivity), Arg.Any<object>());
        await ctx.Received(1)
            .CallActivityAsync<bool>(nameof(RefundPaymentActivity), Arg.Any<object>());
    }

    // ── Notification failure is non-fatal ────────────────────────────────────

    [Fact]
    public async Task RunAsync_WhenNotificationFails_ShouldStillReturnSuccess()
    {
        var ctx = Substitute.For<WorkflowContext>();
        var input = ValidInput();

        ctx.CallActivityAsync<ValidationResult>(nameof(ValidateOrderActivity), Arg.Any<object>())
           .Returns(new ValidationResult(true, null));
        ctx.CallActivityAsync<ReservationResult>(nameof(ReserveInventoryActivity), Arg.Any<object>())
           .Returns(new ReservationResult(true, null));
        ctx.CallActivityAsync<PaymentResult>(nameof(ProcessPaymentActivity), Arg.Any<object>())
           .Returns(new PaymentResult(true, "txn-002", null));
        ctx.CallActivityAsync<NotificationResult>(nameof(SendNotificationActivity), Arg.Any<object>())
           .Returns(new NotificationResult(false));

        var workflow = new OrderSagaWorkflow();
        var result = await workflow.RunAsync(ctx, input);

        result.Success.Should().BeTrue();
    }
}

// ── Arbitraries ──────────────────────────────────────────────────────────────

public static class OrderSagaArbitraries
{
    private static readonly Gen<string> NonEmptyAlphaGen =
        Gen.Choose(3, 12)
           .SelectMany(len =>
               Gen.ArrayOf(Gen.Elements("abcdefghijklmnopqrstuvwxyz".ToCharArray()), len)
                  .Select(chars => new string(chars)));

    public static Arbitrary<OrderSagaInput> OrderSagaInputArbitrary()
    {
        var gen =
            from productId in NonEmptyAlphaGen
            from quantity in Gen.Choose(1, 1000)
            from priceRaw in Gen.Choose(1, 999999)
            from customerId in NonEmptyAlphaGen
            let price = priceRaw / 100m  // yields 0.01 – 9999.99
            select new OrderSagaInput(
                $"order-{Guid.NewGuid():N}",
                productId,
                quantity,
                price,
                customerId);

        return gen.ToArbitrary();
    }
}

// ── Property-based tests ─────────────────────────────────────────────────────

public class OrderSagaWorkflowPropertyTests
{
    // Feature: smart-order, Property 12: Successful saga executes all four activities in order and reaches Completed
    // Validates: Requirements 5.1, 5.2, 5.3, 5.4, 5.5, 5.12, 14.6
    [Property(Arbitrary = new[] { typeof(OrderSagaArbitraries) }, MaxTest = 100)]
    public Property RunAsync_WhenAllActivitiesSucceed_ShouldReturnSuccessAndCallAllActivities(OrderSagaInput input)
    {
        var ctx = Substitute.For<WorkflowContext>();

        ctx.CallActivityAsync<ValidationResult>(nameof(ValidateOrderActivity), Arg.Any<object>())
           .Returns(new ValidationResult(true, null));
        ctx.CallActivityAsync<ReservationResult>(nameof(ReserveInventoryActivity), Arg.Any<object>())
           .Returns(new ReservationResult(true, null));
        ctx.CallActivityAsync<PaymentResult>(nameof(ProcessPaymentActivity), Arg.Any<object>())
           .Returns(new PaymentResult(true, "txn-prop", null));
        ctx.CallActivityAsync<NotificationResult>(nameof(SendNotificationActivity), Arg.Any<object>())
           .Returns(new NotificationResult(true));

        var workflow = new OrderSagaWorkflow();
        var result = workflow.RunAsync(ctx, input).GetAwaiter().GetResult();

        var successIsTrue = result.Success;
        var failureReasonIsNull = result.FailureReason is null;

        var validateCalled = ctx.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name.Contains("CallActivityAsync") &&
                      c.GetArguments().OfType<string>().Any(a => a == nameof(ValidateOrderActivity)));
        var reserveCalled = ctx.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name.Contains("CallActivityAsync") &&
                      c.GetArguments().OfType<string>().Any(a => a == nameof(ReserveInventoryActivity)));
        var paymentCalled = ctx.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name.Contains("CallActivityAsync") &&
                      c.GetArguments().OfType<string>().Any(a => a == nameof(ProcessPaymentActivity)));
        var notifyCalled = ctx.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name.Contains("CallActivityAsync") &&
                      c.GetArguments().OfType<string>().Any(a => a == nameof(SendNotificationActivity)));

        return (successIsTrue && failureReasonIsNull &&
                validateCalled && reserveCalled && paymentCalled && notifyCalled)
            .ToProperty();
    }

    // Feature: smart-order, Property 13: Payment failure triggers compensation and reaches Failed status
    // Validates: Requirements 5.6, 5.7, 5.13, 14.7
    [Property(Arbitrary = new[] { typeof(OrderSagaArbitraries) }, MaxTest = 100)]
    public Property RunAsync_WhenPaymentFailsWithTransactionId_ShouldCallBothCompensationActivities(OrderSagaInput input)
    {
        var ctx = Substitute.For<WorkflowContext>();
        const string transactionId = "txn-partial-prop";

        ctx.CallActivityAsync<ValidationResult>(nameof(ValidateOrderActivity), Arg.Any<object>())
           .Returns(new ValidationResult(true, null));
        ctx.CallActivityAsync<ReservationResult>(nameof(ReserveInventoryActivity), Arg.Any<object>())
           .Returns(new ReservationResult(true, null));
        ctx.CallActivityAsync<PaymentResult>(nameof(ProcessPaymentActivity), Arg.Any<object>())
           .Returns(new PaymentResult(false, transactionId, "Payment gateway error"));
        ctx.CallActivityAsync<bool>(nameof(ReleaseInventoryReservationActivity), Arg.Any<object>())
           .Returns(true);
        ctx.CallActivityAsync<bool>(nameof(RefundPaymentActivity), Arg.Any<object>())
           .Returns(true);

        var workflow = new OrderSagaWorkflow();
        var result = workflow.RunAsync(ctx, input).GetAwaiter().GetResult();

        var successIsFalse = !result.Success;
        var failureReasonNotNull = result.FailureReason is not null;

        var releaseInventoryCalled = ctx.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name.Contains("CallActivityAsync") &&
                      c.GetArguments().OfType<string>().Any(a => a == nameof(ReleaseInventoryReservationActivity)));
        var refundCalled = ctx.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name.Contains("CallActivityAsync") &&
                      c.GetArguments().OfType<string>().Any(a => a == nameof(RefundPaymentActivity)));

        return (successIsFalse && failureReasonNotNull && releaseInventoryCalled && refundCalled)
            .ToProperty();
    }

    // Feature: smart-order, Property 14: Inventory failure skips payment and reaches Failed status
    // Validates: Requirements 5.8
    [Property(Arbitrary = new[] { typeof(OrderSagaArbitraries) }, MaxTest = 100)]
    public Property RunAsync_WhenInventoryFails_ShouldNeverCallPaymentActivity(OrderSagaInput input)
    {
        var ctx = Substitute.For<WorkflowContext>();

        ctx.CallActivityAsync<ValidationResult>(nameof(ValidateOrderActivity), Arg.Any<object>())
           .Returns(new ValidationResult(true, null));
        ctx.CallActivityAsync<ReservationResult>(nameof(ReserveInventoryActivity), Arg.Any<object>())
           .Returns(new ReservationResult(false, "Out of stock"));

        var workflow = new OrderSagaWorkflow();
        var result = workflow.RunAsync(ctx, input).GetAwaiter().GetResult();

        var successIsFalse = !result.Success;

        var paymentNeverCalled = !ctx.ReceivedCalls()
            .Any(c => c.GetMethodInfo().Name.Contains("CallActivityAsync") &&
                      c.GetArguments().OfType<string>().Any(a => a == nameof(ProcessPaymentActivity)));

        return (successIsFalse && paymentNeverCalled).ToProperty();
    }
}
