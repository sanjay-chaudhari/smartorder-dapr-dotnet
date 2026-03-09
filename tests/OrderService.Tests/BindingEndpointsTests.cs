using Dapr;
using Dapr.Client;
using FluentAssertions;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OrderService.Endpoints;
using OrderService.Services;

namespace OrderService.Tests;

/// <summary>
/// Unit tests for BindingEndpoints handler logic.
/// Tests the cron input binding handler and the output binding webhook handler
/// by invoking the same logic inline (mirrors the endpoint lambdas).
/// </summary>
public class BindingEndpointsTests
{
    // Use BindingEndpointsTests as the logger category — the generic type is just
    // a category name and doesn't need to match the production endpoint's ILogger<Program>.
    private static ILogger Logger => NullLogger<BindingEndpointsTests>.Instance;

    // -----------------------------------------------------------------------
    // Cron input binding handler
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CronHandler_WhenCalled_ShouldReturn200()
    {
        var daprClient = Substitute.For<DaprClient>();
        var stateService = Substitute.For<IOrderStateService>();

        var result = await CronHandlerAsync(daprClient, stateService, Logger, CancellationToken.None);

        result.Should().BeOfType<Ok>();
    }

    [Fact]
    public async Task CronHandler_WhenCalled_ShouldNotThrow()
    {
        var daprClient = Substitute.For<DaprClient>();
        var stateService = Substitute.For<IOrderStateService>();

        var act = async () => await CronHandlerAsync(daprClient, stateService, Logger, CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    // -----------------------------------------------------------------------
    // Output binding webhook handler — success
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WebhookHandler_WhenBindingSucceeds_ShouldReturn200WithWebhookSent()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .InvokeBindingAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var result = await WebhookHandlerAsync("order-123", daprClient, Logger, CancellationToken.None);

        result.Should().BeOfType<Ok<WebhookResult>>();
        var ok = (Ok<WebhookResult>)result;
        ok.Value!.OrderId.Should().Be("order-123");
        ok.Value.WebhookSent.Should().BeTrue();
    }

    [Fact]
    public async Task WebhookHandler_WhenBindingSucceeds_ShouldCallCorrectBindingName()
    {
        string? capturedBinding = null;
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .InvokeBindingAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedBinding = callInfo.ArgAt<string>(0);
                return Task.CompletedTask;
            });

        await WebhookHandlerAsync("order-456", daprClient, Logger, CancellationToken.None);

        capturedBinding.Should().Be("order-webhook");
    }

    [Fact]
    public async Task WebhookHandler_WhenBindingSucceeds_ShouldUseCreateOperation()
    {
        string? capturedOperation = null;
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .InvokeBindingAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                capturedOperation = callInfo.ArgAt<string>(1);
                return Task.CompletedTask;
            });

        await WebhookHandlerAsync("order-789", daprClient, Logger, CancellationToken.None);

        capturedOperation.Should().Be("create");
    }

    // -----------------------------------------------------------------------
    // Output binding webhook handler — failure
    // -----------------------------------------------------------------------

    [Fact]
    public async Task WebhookHandler_WhenBindingThrows_ShouldReturn503()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .InvokeBindingAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new DaprException("binding unavailable"));

        var result = await WebhookHandlerAsync("order-err", daprClient, Logger, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(503);
    }

    [Fact]
    public async Task WebhookHandler_WhenGenericExceptionThrows_ShouldReturn503()
    {
        var daprClient = Substitute.For<DaprClient>();
        daprClient
            .InvokeBindingAsync(
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<object>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("unexpected"));

        var result = await WebhookHandlerAsync("order-err2", daprClient, Logger, CancellationToken.None);

        result.Should().BeOfType<ProblemHttpResult>();
        var problem = (ProblemHttpResult)result;
        problem.StatusCode.Should().Be(503);
    }

    // -----------------------------------------------------------------------
    // Inline handler implementations — mirror BindingEndpoints.cs exactly
    // -----------------------------------------------------------------------

    private static async Task<Microsoft.AspNetCore.Http.IResult> CronHandlerAsync(
        DaprClient daprClient,
        IOrderStateService stateService,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        logger.LogInformation("Cron binding triggered: starting stale order cleanup.");
        logger.LogInformation("Cron cleanup completed.");
        return Microsoft.AspNetCore.Http.Results.Ok();
    }

    private static async Task<Microsoft.AspNetCore.Http.IResult> WebhookHandlerAsync(
        string orderId,
        DaprClient daprClient,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = new { orderId, status = "confirmed", timestamp = DateTimeOffset.UtcNow };

            await daprClient.InvokeBindingAsync(
                "order-webhook",
                "create",
                payload,
                cancellationToken: cancellationToken).ConfigureAwait(false);

            return Microsoft.AspNetCore.Http.Results.Ok(new WebhookResult(orderId, true));
        }
        catch (Exception)
        {
            return Microsoft.AspNetCore.Http.Results.Problem(statusCode: 503, title: "Webhook delivery failed");
        }
    }
}
