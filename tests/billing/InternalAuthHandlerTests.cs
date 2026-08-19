using System.Net;
using Billing.Api.Infrastructure;
using Billing.Api.Options;
using Microsoft.AspNetCore.Http;

namespace Billing.Api.Tests;

public sealed class InternalAuthHandlerTests
{
    [Fact]
    public async Task Adds_bearer_token_and_incoming_correlation_id()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[InternalRequestHeaders.CorrelationId] = "correlation-123";
        HttpRequestMessage? captured = null;
        var handler = new InternalAuthHandler(
            Microsoft.Extensions.Options.Options.Create(new InternalAuthOptions { Token = "secret" }),
            new HttpContextAccessor { HttpContext = context })
        {
            InnerHandler = new DelegateHandler(request =>
            {
                captured = request;
                return new HttpResponseMessage(HttpStatusCode.OK);
            })
        };

        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("http://inventory/api/products");

        Assert.Equal("Bearer", captured!.Headers.Authorization!.Scheme);
        Assert.Equal("secret", captured.Headers.Authorization.Parameter);
        Assert.Equal("correlation-123", captured.Headers.GetValues(InternalRequestHeaders.CorrelationId).Single());
    }

    [Fact]
    public async Task Empty_token_and_missing_correlation_id_add_no_internal_headers()
    {
        HttpRequestMessage? captured = null;
        var handler = new InternalAuthHandler(
            Microsoft.Extensions.Options.Options.Create(new InternalAuthOptions { Token = "" }),
            new HttpContextAccessor())
        {
            InnerHandler = new DelegateHandler(request =>
            {
                captured = request;
                return new HttpResponseMessage(HttpStatusCode.OK);
            })
        };

        using var client = new HttpClient(handler);
        using var response = await client.GetAsync("http://inventory/api/products");

        Assert.Null(captured!.Headers.Authorization);
        Assert.False(captured.Headers.Contains(InternalRequestHeaders.CorrelationId));
    }

    [Fact]
    public void Mcp_header_builder_uses_the_same_internal_headers()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[InternalRequestHeaders.CorrelationId] = "mcp-run";

        var headers = InternalRequestHeaders.Build("mcp-secret", context);

        Assert.Equal("Bearer mcp-secret", headers["Authorization"]);
        Assert.Equal("mcp-run", headers[InternalRequestHeaders.CorrelationId]);
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
