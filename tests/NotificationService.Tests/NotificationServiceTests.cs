// Feature: smart-order, Property 7: NotificationService publishes NotificationSentEvent after processing
// Validates: Requirements 3.4
// Feature: smart-order, Property 17: Secrets are retrieved via DaprClient, never hardcoded
// Validates: Requirements 6.3

using Dapr.Client;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.Extensions.Logging.Abstractions;
using NotificationService.Models;
using NSubstitute;

namespace NotificationService.Tests;

// ---------------------------------------------------------------------------
// Arbitraries
// ---------------------------------------------------------------------------
public static class NotificationServiceArbitraries
{
    /// <summary>
    /// Generates valid SendNotificationRequest values with non-empty fields.
    /// </summary>
    public static Arbitrary<SendNotificationRequest> ValidSendNotificationRequest()
    {
        var gen =
            from orderId in Gen.Fresh(() => $"order-{Guid.NewGuid():N}")
            from customerId in Gen.Fresh(() => $"customer-{Guid.NewGuid():N}")
            from message in Gen.Fresh(() => $"Your order has been placed.")
            select new SendNotificationRequest(orderId, customerId, message);

        return gen.ToArbitrary();
    }
}

// ---------------------------------------------------------------------------
// NotificationServiceTests
// ---------------------------------------------------------------------------
public class NotificationServiceTests
{
    private const string PubSubName = "pubsub";
    private const string TopicName = "notification-sent";
    private const string SecretStoreName = "secretstore";
    private const string SecretKeyName = "smtp-password";

    private static DaprClient BuildDaprClientWithSecret(string secretValue = "test-smtp-password")
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

    private static Services.NotificationService CreateService(DaprClient? daprClient = null)
    {
        daprClient ??= BuildDaprClientWithSecret();
        return new Services.NotificationService(daprClient, NullLogger<Services.NotificationService>.Instance);
    }

    // -----------------------------------------------------------------------
    // Property 7: NotificationService publishes NotificationSentEvent after processing
    // Validates: Requirements 3.4
    // -----------------------------------------------------------------------

    /// <summary>
    /// For any valid SendNotificationRequest, PublishEventAsync must be called exactly once
    /// with component "pubsub" and topic "notification-sent" after processing.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NotificationServiceArbitraries) }, MaxTest = 100)]
    public Property SendNotificationAsync_WhenRequestIsValid_ShouldPublishNotificationSentEvent(
        SendNotificationRequest request)
    {
        var daprClient = BuildDaprClientWithSecret();
        var sut = CreateService(daprClient);

        sut.SendNotificationAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        daprClient.Received(1).PublishEventAsync(
            PubSubName,
            TopicName,
            Arg.Any<NotificationSentEvent>(),
            Arg.Any<CancellationToken>());

        return true.ToProperty();
    }

    /// <summary>
    /// For any valid SendNotificationRequest, the published NotificationSentEvent must contain
    /// the same OrderId as the original request.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NotificationServiceArbitraries) }, MaxTest = 100)]
    public Property SendNotificationAsync_WhenRequestIsValid_ShouldPublishEventWithMatchingOrderId(
        SendNotificationRequest request)
    {
        NotificationSentEvent? capturedEvent = null;

        var daprClient = BuildDaprClientWithSecret();
        daprClient
            .PublishEventAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<NotificationSentEvent>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedEvent = callInfo.ArgAt<NotificationSentEvent>(2);
                return Task.CompletedTask;
            });

        var sut = CreateService(daprClient);
        sut.SendNotificationAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        return (capturedEvent is not null && capturedEvent.OrderId == request.OrderId)
            .ToProperty();
    }

    /// <summary>
    /// For any valid SendNotificationRequest, the published event must use component "pubsub"
    /// and topic "notification-sent" — never any other component or topic name.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NotificationServiceArbitraries) }, MaxTest = 100)]
    public Property SendNotificationAsync_WhenRequestIsValid_ShouldUseCorrectPubSubComponentAndTopic(
        SendNotificationRequest request)
    {
        string? capturedComponent = null;
        string? capturedTopic = null;

        var daprClient = BuildDaprClientWithSecret();
        daprClient
            .PublishEventAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<NotificationSentEvent>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedComponent = callInfo.ArgAt<string>(0);
                capturedTopic = callInfo.ArgAt<string>(1);
                return Task.CompletedTask;
            });

        var sut = CreateService(daprClient);
        sut.SendNotificationAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        return (capturedComponent == PubSubName && capturedTopic == TopicName)
            .ToProperty();
    }

    // -----------------------------------------------------------------------
    // Property 17: Secrets are retrieved via DaprClient, never hardcoded
    // Validates: Requirements 6.3
    // -----------------------------------------------------------------------

    /// <summary>
    /// For any valid SendNotificationRequest, GetSecretAsync must be called with
    /// component "secretstore" and key "smtp-password" before the notification is sent.
    /// This verifies the secret is always fetched via DaprClient, never hardcoded.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NotificationServiceArbitraries) }, MaxTest = 100)]
    public Property SendNotificationAsync_WhenCalled_ShouldRetrieveSecretFromDaprSecretStore(
        SendNotificationRequest request)
    {
        var daprClient = BuildDaprClientWithSecret();
        var sut = CreateService(daprClient);

        sut.SendNotificationAsync(request, CancellationToken.None).GetAwaiter().GetResult();

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
    [Property(Arbitrary = new[] { typeof(NotificationServiceArbitraries) }, MaxTest = 100)]
    public Property SendNotificationAsync_WhenCalledMultipleTimes_ShouldRetrieveSecretOnlyOnce(
        SendNotificationRequest request)
    {
        var daprClient = BuildDaprClientWithSecret();
        var sut = CreateService(daprClient);

        // Call twice on the same service instance
        sut.SendNotificationAsync(request, CancellationToken.None).GetAwaiter().GetResult();
        sut.SendNotificationAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        // Secret must only be fetched once due to lazy caching
        daprClient.Received(1).GetSecretAsync(
            SecretStoreName,
            SecretKeyName,
            Arg.Any<IReadOnlyDictionary<string, string>>(),
            Arg.Any<CancellationToken>());

        return true.ToProperty();
    }

    /// <summary>
    /// GetSecretAsync must always be called with component "secretstore" and key "smtp-password".
    /// This verifies the correct store name and key are used, never any hardcoded alternative.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(NotificationServiceArbitraries) }, MaxTest = 100)]
    public Property SendNotificationAsync_WhenCalled_ShouldUseCorrectSecretStoreAndKey(
        SendNotificationRequest request)
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
                return new Dictionary<string, string> { [SecretKeyName] = "test-smtp-password" };
            });

        var sut = CreateService(daprClient);
        sut.SendNotificationAsync(request, CancellationToken.None).GetAwaiter().GetResult();

        return (capturedStoreName == SecretStoreName && capturedKeyName == SecretKeyName)
            .ToProperty();
    }
}
