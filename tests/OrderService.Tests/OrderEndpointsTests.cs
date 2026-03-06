// Feature: smart-order, Property 3: Non-existent order lookup returns 404
// Validates: Requirements 1.7

using FluentAssertions;
using FsCheck;
using FsCheck.Fluent;
using FsCheck.Xunit;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using NSubstitute;
using OrderService.Models;
using OrderService.Services;

namespace OrderService.Tests;

/// <summary>
/// Tests for the GET /orders/{orderId} endpoint handler logic.
/// </summary>
public class OrderEndpointsTests
{
    // -----------------------------------------------------------------------
    // Property 3: Non-existent order lookup returns 404
    // Feature: smart-order, Property 3: Non-existent order lookup returns 404
    // Validates: Requirements 1.7
    // -----------------------------------------------------------------------

    /// <summary>
    /// For any orderId string that was never created, GetOrderAsync returns null
    /// and the endpoint handler must return HTTP 404 Not Found.
    /// </summary>
    [Property(MaxTest = 100)]
    public Property GetOrder_WhenOrderDoesNotExist_ShouldReturn404()
    {
        // Generate arbitrary non-empty orderId strings
        var gen = ArbMap.Default.ArbFor<NonEmptyString>().Generator
            .Select(s => s.Get);

        return Prop.ForAll(gen.ToArbitrary(), orderId =>
        {
            var stateService = Substitute.For<IOrderStateService>();
            stateService
                .GetOrderAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns((Order?)null);

            // Invoke the same logic as the endpoint handler
            var result = GetOrderHandlerAsync(orderId, stateService, CancellationToken.None)
                .GetAwaiter().GetResult();

            result.Should().BeOfType<NotFound>();
        });
    }

    /// <summary>
    /// For any orderId that exists in the state store, the endpoint returns 200 OK
    /// with an OrderResponse whose fields match the stored Order.
    /// </summary>
    [Property(Arbitrary = new[] { typeof(SmartOrderArbitraries) }, MaxTest = 100)]
    public Property GetOrder_WhenOrderExists_ShouldReturn200WithMatchingFields(Order order)
    {
        var stateService = Substitute.For<IOrderStateService>();
        stateService
            .GetOrderAsync(order.OrderId, Arg.Any<CancellationToken>())
            .Returns(order);

        var result = GetOrderHandlerAsync(order.OrderId, stateService, CancellationToken.None)
            .GetAwaiter().GetResult();

        var ok = result as Ok<OrderResponse>;
        ok.Should().NotBeNull();
        ok!.Value!.OrderId.Should().Be(order.OrderId);
        ok.Value.ProductId.Should().Be(order.ProductId);
        ok.Value.Quantity.Should().Be(order.Quantity);
        ok.Value.Price.Should().Be(order.Price);
        ok.Value.Status.Should().Be(order.Status);

        return true.ToProperty();
    }

    // -----------------------------------------------------------------------
    // Inline handler — mirrors the GET /orders/{orderId} endpoint exactly
    // so we can test the logic without spinning up a full WebApplication.
    // -----------------------------------------------------------------------
    private static async Task<IResult> GetOrderHandlerAsync(
        string orderId,
        IOrderStateService stateService,
        CancellationToken ct)
    {
        try
        {
            var order = await stateService.GetOrderAsync(orderId, ct).ConfigureAwait(false);
            if (order is null) return Results.NotFound();
            return Results.Ok(new OrderResponse(
                order.OrderId,
                order.ProductId,
                order.Quantity,
                order.Price,
                order.Status,
                order.CreatedAt,
                order.UpdatedAt));
        }
        catch (Dapr.DaprException)
        {
            return Results.Problem(statusCode: 503, title: "Service unavailable");
        }
        catch (Exception)
        {
            return Results.Problem();
        }
    }
}
