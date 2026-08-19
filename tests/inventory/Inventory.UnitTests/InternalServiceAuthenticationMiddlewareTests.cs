using Inventory.Api.Security;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using Microsoft.Net.Http.Headers;

namespace Inventory.UnitTests;

public sealed class InternalServiceAuthenticationMiddlewareTests
{
    [Theory]
    [InlineData("/mcp")]
    [InlineData("/mcp/tools")]
    [InlineData("/api/stock/debits")]
    [InlineData("/api/stock/debits/7db75dda-3930-49c2-a275-771ca29a383d")]
    public async Task ValidBearerTokenAllowsProtectedRequest(string path)
    {
        var nextCalls = 0;
        var middleware = CreateMiddleware("expected-secret", _ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });
        var context = CreateContext(path, "Bearer expected-secret");

        await middleware.InvokeAsync(context);

        Assert.Equal(1, nextCalls);
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic expected-secret")]
    [InlineData("Bearer")]
    [InlineData("Bearer ")]
    [InlineData("Bearer wrong-secret")]
    [InlineData("Bearer expected-secret ")]
    public async Task MissingOrInvalidAuthorizationRejectsProtectedRequest(string? authorization)
    {
        var nextCalls = 0;
        var middleware = CreateMiddleware("expected-secret", _ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });
        var context = CreateContext("/api/stock/debits", authorization);

        await middleware.InvokeAsync(context);

        Assert.Equal(0, nextCalls);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        Assert.Equal("Bearer", context.Response.Headers.WWWAuthenticate);
        Assert.Equal("no-store", context.Response.Headers.CacheControl);
        Assert.Equal(0, context.Response.ContentLength);
        Assert.Equal(0, context.Response.Body.Length);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public async Task EmptyConfiguredTokenBypassesAuthentication(string? configuredToken)
    {
        var nextCalls = 0;
        var middleware = CreateMiddleware(configuredToken, _ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });
        var context = CreateContext("/mcp", null);

        await middleware.InvokeAsync(context);

        Assert.Equal(1, nextCalls);
    }

    [Theory]
    [InlineData("/api/products")]
    [InlineData("/health")]
    [InlineData("/mcp-impersonator")]
    [InlineData("/api/stock/debits-impersonator")]
    public async Task PublicOrPrefixConfusionPathBypassesAuthentication(string path)
    {
        var nextCalls = 0;
        var middleware = CreateMiddleware("expected-secret", _ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });
        var context = CreateContext(path, null);

        await middleware.InvokeAsync(context);

        Assert.Equal(1, nextCalls);
    }

    [Fact]
    public async Task MultipleAuthorizationHeadersAreRejected()
    {
        var nextCalls = 0;
        var middleware = CreateMiddleware("expected-secret", _ =>
        {
            nextCalls++;
            return Task.CompletedTask;
        });
        var context = CreateContext("/mcp", null);
        context.Request.Headers.Append(HeaderNames.Authorization, "Bearer expected-secret");
        context.Request.Headers.Append(HeaderNames.Authorization, "Bearer expected-secret");

        await middleware.InvokeAsync(context);

        Assert.Equal(0, nextCalls);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    private static InternalServiceAuthenticationMiddleware CreateMiddleware(
        string? configuredToken,
        RequestDelegate next) =>
        new(
            next,
            Options.Create(new InternalAuthOptions
            {
                Token = configuredToken ?? string.Empty
            }));

    private static DefaultHttpContext CreateContext(string path, string? authorization)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.Body = new MemoryStream();
        if (authorization is not null)
        {
            context.Request.Headers[HeaderNames.Authorization] = authorization;
        }

        return context;
    }
}
