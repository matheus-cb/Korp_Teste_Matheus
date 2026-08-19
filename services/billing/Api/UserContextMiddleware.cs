using Billing.Api.Application;

namespace Billing.Api.Api;

/// <summary>
/// Resolve o operador da requisição e exige autenticação nas rotas de negócio.
/// Login, health e OpenAPI ficam abertos; o resto responde 401 sem sessão.
/// </summary>
public sealed class UserContextMiddleware(RequestDelegate next)
{
    private const string ItemKey = "notaflow.current-user";

    private static readonly string[] AnonymousPrefixes =
    [
        "/api/auth/login",
        "/health",
        "/openapi",
    ];

    public async Task InvokeAsync(HttpContext context, AuthService auth)
    {
        var token = AuthEndpoints.ReadToken(context);
        if (!string.IsNullOrEmpty(token))
        {
            var user = await auth.ResolveAsync(token, context.RequestAborted);
            if (user is not null)
            {
                context.Items[ItemKey] = user;
            }
        }

        if (context.Items.ContainsKey(ItemKey) || IsAnonymous(context.Request.Path))
        {
            await next(context);
            return;
        }

        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        context.Response.Headers.WWWAuthenticate = "Bearer";
        await context.Response.WriteAsJsonAsync(new
        {
            type = "https://httpstatuses.com/401",
            title = "Autenticação necessária.",
            status = StatusCodes.Status401Unauthorized,
            code = "UNAUTHENTICATED",
            traceId = context.TraceIdentifier,
        });
    }

    /// <summary>Comparação por segmento: `/health` não pode liberar `/healthz-evil`.</summary>
    private static bool IsAnonymous(PathString path)
    {
        foreach (var prefix in AnonymousPrefixes)
        {
            if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    public static CurrentUser? Get(HttpContext context) =>
        context.Items.TryGetValue(ItemKey, out var value) ? value as CurrentUser : null;
}

public static class HttpContextUserExtensions
{
    public static CurrentUser? GetCurrentUser(this HttpContext context) =>
        UserContextMiddleware.Get(context);

    /// <summary>Nome a gravar na autoria da operação.</summary>
    public static string ActingUserName(this IHttpContextAccessor accessor) =>
        accessor.HttpContext?.GetCurrentUser()?.DisplayName ?? "sistema";
}
