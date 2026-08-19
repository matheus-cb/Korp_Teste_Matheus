using System.Net;
using System.Text;
using Billing.Api.Infrastructure;

namespace Billing.Api.Tests;

public sealed class InventoryClientTests
{
    [Fact]
    public async Task Debit_sends_attempt_as_idempotency_key()
    {
        var attemptId = Guid.NewGuid();
        HttpRequestMessage? captured = null;
        var handler = new DelegateHandler(async request =>
        {
            captured = new HttpRequestMessage(request.Method, request.RequestUri);
            foreach (var header in request.Headers) captured.Headers.TryAddWithoutValidation(header.Key, header.Value);
            captured.Content = new StringContent(await request.Content!.ReadAsStringAsync(), Encoding.UTF8, "application/json");
            return Json(HttpStatusCode.OK, $"{{\"operationId\":\"{Guid.NewGuid()}\",\"state\":\"Completed\"}}");
        });
        var client = new InventoryClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://inventory") },
            TestLoggers.For<InventoryClient>());

        var result = await client.DebitAsync(
            attemptId, Guid.NewGuid(), [new(Guid.NewGuid(), 1)], CancellationToken.None);

        Assert.True(result.IsCompleted);
        Assert.Equal(attemptId.ToString(), captured!.Headers.GetValues("Idempotency-Key").Single());
    }

    [Fact]
    public async Task Conflict_is_mapped_to_rejected_outcome()
    {
        var handler = new DelegateHandler(_ => Task.FromResult(Json(
            HttpStatusCode.Conflict,
            $"{{\"operationId\":\"{Guid.NewGuid()}\",\"state\":\"Rejected\",\"errorCode\":\"INSUFFICIENT_STOCK\",\"errorMessage\":\"Sem saldo\"}}")));
        var client = new InventoryClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://inventory") },
            TestLoggers.For<InventoryClient>());

        var result = await client.DebitAsync(
            Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), 1)], CancellationToken.None);

        Assert.True(result.IsRejected);
        Assert.Equal("INSUFFICIENT_STOCK", result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, "")]
    [InlineData(HttpStatusCode.OK, "not-json")]
    [InlineData(HttpStatusCode.OK, "{\"state\":\"Completed\"}")]
    [InlineData(HttpStatusCode.NoContent, "")]
    public async Task Success_without_a_valid_explicit_outcome_is_unavailable(HttpStatusCode status, string body)
    {
        var handler = new DelegateHandler(_ => Task.FromResult(Json(status, body)));
        var client = new InventoryClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://inventory") },
            TestLoggers.For<InventoryClient>());

        var exception = await Assert.ThrowsAsync<Billing.Api.Domain.DependencyUnavailableException>(() =>
            client.DebitAsync(Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), 1)], CancellationToken.None));

        Assert.Equal("INVENTORY_UNAVAILABLE", exception.Code);
    }

    [Fact]
    public async Task Problem_details_conflict_preserves_the_inventory_error_code()
    {
        var handler = new DelegateHandler(_ => Task.FromResult(Json(
            HttpStatusCode.Conflict,
            "{\"code\":\"IDEMPOTENCY_KEY_REUSED\",\"detail\":\"Different payload\"}")));
        var client = new InventoryClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://inventory") },
            TestLoggers.For<InventoryClient>());

        var result = await client.DebitAsync(
            Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), 1)], CancellationToken.None);

        Assert.True(result.IsRejected);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", result.ErrorCode);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "INVENTORY_REQUEST_REJECTED")]
    [InlineData(HttpStatusCode.Unauthorized, "INVENTORY_AUTH_FAILED")]
    [InlineData(HttpStatusCode.NotFound, "INVENTORY_ENDPOINT_NOT_FOUND")]
    public async Task Permanent_client_errors_do_not_enter_an_infinite_retry(HttpStatusCode status, string expectedCode)
    {
        var handler = new DelegateHandler(_ => Task.FromResult(Json(status, "")));
        var client = new InventoryClient(
            new HttpClient(handler) { BaseAddress = new Uri("http://inventory") },
            TestLoggers.For<InventoryClient>());

        var result = await client.DebitAsync(
            Guid.NewGuid(), Guid.NewGuid(), [new(Guid.NewGuid(), 1)], CancellationToken.None);

        Assert.True(result.IsRejected);
        Assert.Equal(expectedCode, result.ErrorCode);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    private sealed class DelegateHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => handler(request);
    }
}
