using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Inventory.Api.Security;

public sealed class InternalAuthOptions
{
    public const string SectionName = "InternalAuth";

    public string Token { get; init; } = string.Empty;
    public bool AllowUnauthenticated { get; init; }
}

public sealed class InternalServiceAuthenticationMiddleware
{
    private const string BearerPrefix = "Bearer ";
    private readonly RequestDelegate next;
    private readonly byte[]? configuredTokenDigest;

    public InternalServiceAuthenticationMiddleware(
        RequestDelegate next,
        IOptions<InternalAuthOptions> options)
    {
        this.next = next;
        configuredTokenDigest = string.IsNullOrEmpty(options.Value.Token)
            ? null
            : ComputeDigest(options.Value.Token);
    }

    public Task InvokeAsync(HttpContext context)
    {
        if (configuredTokenDigest is null || !RequiresInternalAuthentication(context.Request.Path))
        {
            return next(context);
        }

        if (!TryReadBearerToken(context.Request.Headers[HeaderNames.Authorization], out var presentedToken) ||
            !CryptographicOperations.FixedTimeEquals(
                configuredTokenDigest,
                ComputeDigest(presentedToken)))
        {
            return RejectAsync(context);
        }

        return next(context);
    }

    /// <summary>
    /// Lista explicita, casada por SEGMENTO: `/mcp` nao pode liberar `/mcp-evil`
    /// nem `/api/stock/debits` liberar `/api/stock/debits-impersonator`.
    /// </summary>
    private static bool RequiresInternalAuthentication(PathString path) =>
        path.StartsWithSegments("/mcp", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/stock/debits", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWithSegments("/api/stock/reconciliation", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadBearerToken(StringValues authorizationValues, out string token)
    {
        token = string.Empty;
        if (authorizationValues.Count != 1)
        {
            return false;
        }

        var authorization = authorizationValues[0];
        if (authorization is null ||
            !authorization.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        token = authorization[BearerPrefix.Length..];
        return token.Length > 0 && !token.Any(char.IsWhiteSpace);
    }

    private static byte[] ComputeDigest(string token) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(token));

    private static Task RejectAsync(HttpContext context)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers[HeaderNames.WWWAuthenticate] = "Bearer";
        context.Response.Headers[HeaderNames.CacheControl] = "no-store";
        context.Response.ContentLength = 0;
        return Task.CompletedTask;
    }
}
