using PaymentService.Models;

namespace PaymentService.Services;

public interface IPaymentService
{
    Task<ProcessPaymentResponse> ProcessPaymentAsync(ProcessPaymentRequest request, CancellationToken cancellationToken);
    Task<RefundPaymentResponse> RefundAsync(RefundPaymentRequest request, CancellationToken cancellationToken);
    Task InitializeAsync(CancellationToken cancellationToken);
}
