using System.Diagnostics;

namespace Inventory.Api.Security;

public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
    public const string HeaderName = "X-Correlation-ID";

    public async Task InvokeAsync(HttpContext context)
    {
        var presented = context.Request.Headers[HeaderName].FirstOrDefault();
        var correlationId = Guid.TryParse(presented, out var parsed) && parsed != Guid.Empty
            ? parsed.ToString()
            : Guid.NewGuid().ToString();

        context.TraceIdentifier = correlationId;
        context.Request.Headers[HeaderName] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;
        Activity.Current?.SetTag("correlation.id", correlationId);
        await next(context);
    }
}
