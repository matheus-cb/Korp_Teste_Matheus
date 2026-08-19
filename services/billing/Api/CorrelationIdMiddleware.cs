using System.Diagnostics;
using Billing.Api.Infrastructure;

namespace Billing.Api.Api;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var presented = context.Request.Headers[InternalRequestHeaders.CorrelationId].FirstOrDefault();
        var correlationId = Guid.TryParse(presented, out var parsed) && parsed != Guid.Empty
            ? parsed.ToString()
            : Guid.NewGuid().ToString();

        context.TraceIdentifier = correlationId;
        context.Request.Headers[InternalRequestHeaders.CorrelationId] = correlationId;
        context.Response.Headers[InternalRequestHeaders.CorrelationId] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);
        await next(context);
    }
}
