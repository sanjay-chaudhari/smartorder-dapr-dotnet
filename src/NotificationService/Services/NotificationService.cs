using System.Diagnostics;
using Dapr;
using Dapr.Client;
using NotificationService.Models;

namespace NotificationService.Services;

public class NotificationService : INotificationService
{
    private string? _smtpPassword;
    private readonly SemaphoreSlim _initLock = new(1, 1);
    private readonly DaprClient _daprClient;
    private readonly ILogger<NotificationService> _logger;

    public NotificationService(DaprClient daprClient, ILogger<NotificationService> logger)
    {
        _daprClient = daprClient;
        _logger = logger;
    }

    // Kept for interface compatibility — no-op since we lazy-load on first use
    public Task InitializeAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_smtpPassword is not null) return;

        await _initLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_smtpPassword is not null) return;

            var secrets = await _daprClient
                .GetSecretAsync("secretstore", "smtp-password", cancellationToken: cancellationToken)
                .ConfigureAwait(false);

            _smtpPassword = secrets.TryGetValue("smtp-password", out var pwd) ? pwd : string.Empty;

            _logger.LogInformation(
                "NotificationService initialized. ServiceName={ServiceName}", "notification-service");
        }
        catch (DaprException ex)
        {
            _logger.LogError(ex,
                "Failed to retrieve secret. ServiceName={ServiceName}, SecretName={SecretName}",
                "notification-service", "smtp-password");
            throw;
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async Task SendNotificationAsync(
        SendNotificationRequest request,
        CancellationToken cancellationToken)
    {
        await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);

        using (_logger.BeginScope(new Dictionary<string, object>
        {
            ["ServiceName"] = "notification-service",
            ["OperationName"] = "SendNotification",
            ["OrderId"] = request.OrderId,
            ["TraceId"] = Activity.Current?.TraceId.ToString() ?? string.Empty,
            ["SpanId"] = Activity.Current?.SpanId.ToString() ?? string.Empty
        }))
        {
            try
            {
                _logger.LogInformation(
                    "Sending notification to customer {CustomerId} for order {OrderId}",
                    request.CustomerId, request.OrderId);

                await Task.CompletedTask.ConfigureAwait(false);

                var sentEvent = new NotificationSentEvent(
                    request.OrderId,
                    request.CustomerId,
                    DateTimeOffset.UtcNow);

                await _daprClient
                    .PublishEventAsync("pubsub", "notification-sent", sentEvent, cancellationToken)
                    .ConfigureAwait(false);

                _logger.LogInformation(
                    "Notification sent and event published for order {OrderId}", request.OrderId);
            }
            catch (DaprException ex)
            {
                _logger.LogError(ex,
                    "Dapr error sending notification for {OrderId}. ServiceName={ServiceName}, OperationName={OperationName}",
                    request.OrderId, "notification-service", "SendNotification");
                throw;
            }
        }
    }
}
