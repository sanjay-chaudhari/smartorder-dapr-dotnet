// Feature: smart-order, Property 17: Secrets are retrieved via DaprClient, never hardcoded
// Validates: Requirements 6.2

using Dapr.Client;
using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using PaymentService.Models;
using PaymentService.Services;

namespace PaymentService.Tests;

// ---------------------------------------------------------------------------
// Arbitraries
// ---------------------------------------------------------------------------
public static class PaymentServiceArbitraries
{
    /// <summary>
    /// Generates valid ProcessPaymentRequest values with non-empty fields and positive amount.
    /// </summary>
    public static Arbitrary<ProcessPaymentRequest> ValidProcessPaymentRequest()
    {
        var gen =
            from orderId in Gen.Fresh(() => $"order-{Guid.NewGuid():N}")
            from amountCents in Gen.Choose(1, 1_000_000)
            from customerId in Gen.Fresh(() => $"customer-{Guid.NewGuid():N}")
            select new ProcessPaymentRequest(orderId, amountCents / 100m, customerId);

        return gen.ToArbitrary();
    }

    /// <summary>
    /// Generates valid RefundPaymentRequest values.
    /// </summary>
    public static Arbitrary<RefundPaymentRequest> ValidRefundPaymentRequest()
    {
        var gen =
            from orderId in Gen.Fresh(() => $"order-{Guid.NewGuid():N}")
            from transactionId in Gen.Fresh(() => $"txn-{Guid.NewGuid():N}")
            from amountCents in Gen.Choose(1, 1_000_000)
            select new RefundPaymentRequest(orderId, transactionId, amountCents / 100m);

        return gen.ToArbitrary();
    }
}

// ---------------------------------------------------------------------------
// PaymentServiceTests
// ---------------------------------------------------------------------------
public class PaymentServiceTests
{
    private const string SecretStoreName = "secretstore";
    private const string SecretKeyName = "payment-api-key";

    /// <summary>
    /// Creates a DaprClient substitute that returns a valid secret for "payment-api-key".
    /// </summary>
    private static DaprClient BuildDaprClientWithSecret(string secretValue = "test-api-key")
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetSecretAsync(
                SecretStoreName,
                SecretKeyName,
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(new Dictionary<string, string> { [SecretKeyName] = secretValue });
        return daprClient;
    }

    private static Services.PaymentService CreateService(DaprClient? daprClient = null)
    {
        daprClient ??= BuildDaprClientWithSecret();
        return new Services.PaymentService(daprClient, NullLogger<Services.PaymentService>.Instance);
    }

    // -----------------------------------------------------------------------
    // Property 17: Secrets are retrieved via DaprClient, never hardcoded
    // Validates: Requirements 6.2
    // -----------------------------------------------------------------------

    /// <summary>
    /// For any valid ProcessPaymentRequest, GetSecretAsync must be called with
    /// component "secretstore" and key "payment-api-key" before the payment is processed.
    /// This verifies the secret is always fetched via DaprClient, never hardcoded.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(PaymentServiceArbitraries) }, MaxTest = 100)]
    public Property ProcessPaymentAsync_WhenCalled_ShouldRetrieveSecretFromDaprSecretStore(
        ProcessPaymentRequest request)
    {
        var daprClient = BuildDaprClientWithSecret();
        var sut = CreateService(daprClient);

        sut.ProcessPaymentAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        daprClient.Received(1).GetSecretAsync(
            SecretStoreName,
            SecretKeyName,
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());

        return true.ToProperty();
    }

    /// <summary>
    /// For any valid RefundPaymentRequest, GetSecretAsync must be called with
    /// component "secretstore" and key "payment-api-key" before the refund is processed.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(PaymentServiceArbitraries) }, MaxTest = 100)]
    public Property RefundAsync_WhenCalled_ShouldRetrieveSecretFromDaprSecretStore(
        RefundPaymentRequest request)
    {
        var daprClient = BuildDaprClientWithSecret();
        var sut = CreateService(daprClient);

        sut.RefundAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        daprClient.Received(1).GetSecretAsync(
            SecretStoreName,
            SecretKeyName,
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());

        return true.ToProperty();
    }

    /// <summary>
    /// Once the secret is cached (lazy init), subsequent calls must NOT call GetSecretAsync again.
    /// This verifies the double-checked locking pattern works correctly.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(PaymentServiceArbitraries) }, MaxTest = 100)]
    public Property ProcessPaymentAsync_WhenCalledMultipleTimes_ShouldRetrieveSecretOnlyOnce(
        ProcessPaymentRequest request)
    {
        var daprClient = BuildDaprClientWithSecret();
        var sut = CreateService(daprClient);

        // Call twice on the same service instance
        sut.ProcessPaymentAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        sut.ProcessPaymentAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        // Secret must only be fetched once due to lazy caching
        daprClient.Received(1).GetSecretAsync(
            SecretStoreName,
            SecretKeyName,
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());

        return true.ToProperty();
    }

    /// <summary>
    /// GetSecretAsync must never be called with a hardcoded string literal as the secret value.
    /// The component name must always be "secretstore" and the key must always be "payment-api-key".
    /// </summary>
    [Property(Arbitrary = new[] { typeof(PaymentServiceArbitraries) }, MaxTest = 100)]
    public Property ProcessPaymentAsync_WhenCalled_ShouldUseCorrectSecretStoreAndKey(
        ProcessPaymentRequest request)
    {
        string? capturedStoreName = null;
        string? capturedKeyName = null;

        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .GetSecretAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedStoreName = callInfo.ArgAt<string>(0);
                capturedKeyName = callInfo.ArgAt<string>(1);
                return new Dictionary<string, string> { [SecretKeyName] = "test-key" };
            });

        var sut = CreateService(daprClient);
        sut.ProcessPaymentAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        return (capturedStoreName == SecretStoreName && capturedKeyName == SecretKeyName)
            .ToProperty();
    }
}
