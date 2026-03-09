using Dapr;
using Dapr.Client;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OrderService.Models;
using OrderService.Services;

namespace OrderService.Tests;

/// <summary>
/// Unit tests for GrpcInventoryClient.
/// GrpcInventoryClient uses CreateInvokeMethodRequest + InvokeMethodAsync(HttpRequestMessage)
/// — both are abstract on DaprClient and fully interceptable by NSubstitute.
/// </summary>
public class GrpcInventoryClientTests
{
    private static (GrpcInventoryClient sut, DaprClient daprClient) Create()
    {
        var daprClient = Substitute.For<DaprClient>();
        // CreateInvokeMethodRequest is abstract — return a dummy HttpRequestMessage
        daprClient
            .CreateInvokeMethodRequest(
                Arg.Any<HttpMethod>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
                Arg.Any<ReserveInventoryRequest>())
            .Returns(new HttpRequestMessage());
        return (new GrpcInventoryClient(daprClient, NullLogger<GrpcInventoryClient>.Instance), daprClient);
    }

    // -----------------------------------------------------------------------
    // ReserveAsync — success path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReserveAsync_WhenInventorySucceeds_ShouldReturnSuccessResponse()
    {
        var (sut, daprClient) = Create();
        daprClient
            .InvokeMethodAsync<ReserveInventoryResponse>(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(new ReserveInventoryResponse(true, null));

        var result = await sut.ReserveAsync(new ReserveInventoryRequest("prod-1", 3, "order-abc"), CancellationToken.None);

        result.Success.Should().BeTrue();
        result.FailureReason.Should().BeNull();
    }

    [Fact]
    public async Task ReserveAsync_WhenInventoryFails_ShouldReturnFailureResponse()
    {
        var (sut, daprClient) = Create();
        daprClient
            .InvokeMethodAsync<ReserveInventoryResponse>(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(new ReserveInventoryResponse(false, "out of stock"));

        var result = await sut.ReserveAsync(new ReserveInventoryRequest("prod-1", 100, "order-abc"), CancellationToken.None);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Be("out of stock");
    }

    // -----------------------------------------------------------------------
    // ReserveAsync — correct parameters passed to CreateInvokeMethodRequest
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReserveAsync_ShouldCallCreateInvokeMethodRequestWithHttpMethodPost()
    {
        var (sut, daprClient) = Create();
        daprClient
            .InvokeMethodAsync<ReserveInventoryResponse>(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(new ReserveInventoryResponse(true, null));

        await sut.ReserveAsync(new ReserveInventoryRequest("prod-1", 1, "order-abc"), CancellationToken.None);

        daprClient.Received(1).CreateInvokeMethodRequest(
            HttpMethod.Post,
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
            Arg.Any<ReserveInventoryRequest>());
    }

    [Fact]
    public async Task ReserveAsync_ShouldCallInventoryServiceAppId()
    {
        var (sut, daprClient) = Create();
        daprClient
            .InvokeMethodAsync<ReserveInventoryResponse>(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(new ReserveInventoryResponse(true, null));

        await sut.ReserveAsync(new ReserveInventoryRequest("prod-1", 1, "order-abc"), CancellationToken.None);

        daprClient.Received(1).CreateInvokeMethodRequest(
            Arg.Any<HttpMethod>(),
            "inventory-service",
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
            Arg.Any<ReserveInventoryRequest>());
    }

    [Fact]
    public async Task ReserveAsync_ShouldCallCorrectMethodPath()
    {
        var (sut, daprClient) = Create();
        daprClient
            .InvokeMethodAsync<ReserveInventoryResponse>(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(new ReserveInventoryResponse(true, null));

        await sut.ReserveAsync(new ReserveInventoryRequest("prod-1", 1, "order-abc"), CancellationToken.None);

        daprClient.Received(1).CreateInvokeMethodRequest(
            Arg.Any<HttpMethod>(),
            Arg.Any<string>(),
            "inventory/reserve",
            Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
            Arg.Any<ReserveInventoryRequest>());
    }

    [Fact]
    public async Task ReserveAsync_ShouldPassRequestDataThrough()
    {
        var (sut, daprClient) = Create();
        daprClient
            .InvokeMethodAsync<ReserveInventoryResponse>(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(new ReserveInventoryResponse(true, null));

        var request = new ReserveInventoryRequest("prod-xyz", 7, "order-999");
        await sut.ReserveAsync(request, CancellationToken.None);

        daprClient.Received(1).CreateInvokeMethodRequest(
            Arg.Any<HttpMethod>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<IReadOnlyCollection<KeyValuePair<string, string>>>(),
            Arg.Is<ReserveInventoryRequest>(r =>
                r.ProductId == "prod-xyz" && r.Quantity == 7 && r.OrderId == "order-999"));
    }

    // -----------------------------------------------------------------------
    // ReserveAsync — exception propagation
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ReserveAsync_WhenDaprExceptionThrown_ShouldPropagate()
    {
        var (sut, daprClient) = Create();
        daprClient
            .InvokeMethodAsync<ReserveInventoryResponse>(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new DaprException("gRPC transport error"));

        var act = async () => await sut.ReserveAsync(
            new ReserveInventoryRequest("prod-1", 1, "order-abc"), CancellationToken.None);

        await act.Should().ThrowAsync<DaprException>().WithMessage("*gRPC transport error*");
    }

    [Fact]
    public async Task ReserveAsync_WhenCancelled_ShouldPropagateCancellation()
    {
        var (sut, daprClient) = Create();
        daprClient
            .InvokeMethodAsync<ReserveInventoryResponse>(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new OperationCanceledException());

        var act = async () => await sut.ReserveAsync(
            new ReserveInventoryRequest("prod-1", 1, "order-abc"), CancellationToken.None);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
