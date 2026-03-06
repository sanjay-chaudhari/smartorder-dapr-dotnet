using FluentValidation;
using OrderService.Models;

namespace OrderService.Services;

public class OrderValidator : AbstractValidator<CreateOrderRequest>
{
    public OrderValidator()
    {
        RuleFor(x => x.ProductId).NotEmpty();
        RuleFor(x => x.Quantity).GreaterThan(0);
        RuleFor(x => x.Price).GreaterThan(0);
    }
}
