using System.Net.Http.Headers;
using Billing.Api.Options;
using Microsoft.Extensions.Options;

namespace Billing.Api.Infrastructure;

public static class InternalRequestHeaders
{
    public const string CorrelationId = "X-Correlation-ID";

    public static Dictionary<string, string> Build(string? token, HttpContext? context)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(token))
            headers["Authorization"] = $"Bearer {token.Trim()}";

        var correlationId = context?.Request.Headers[CorrelationId].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(correlationId))
            headers[CorrelationId] = correlationId;
        return headers;
    }
}

public sealed class InternalAuthHandler(
    IOptions<InternalAuthOptions> options,
    IHttpContextAccessor httpContextAccessor) : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        foreach (var header in InternalRequestHeaders.Build(options.Value.Token, httpContextAccessor.HttpContext))
        {
            if (header.Key.Equals("Authorization", StringComparison.OrdinalIgnoreCase))
                request.Headers.Authorization = AuthenticationHeaderValue.Parse(header.Value);
            else
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
