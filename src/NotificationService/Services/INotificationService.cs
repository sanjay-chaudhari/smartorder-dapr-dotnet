using NotificationService.Models;

namespace NotificationService.Services;

public interface INotificationService
{
    Task SendNotificationAsync(SendNotificationRequest request, CancellationToken cancellationToken);
    Task InitializeAsync(CancellationToken cancellationToken);
}
