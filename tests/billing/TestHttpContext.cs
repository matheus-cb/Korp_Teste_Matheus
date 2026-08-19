using Microsoft.AspNetCore.Http;

namespace Billing.Api.Tests;

/// <summary>
/// Contexto HTTP mínimo para os testes: fornece o operador atuante sem subir
/// o pipeline inteiro. Sem operador, a autoria cai para "sistema".
/// </summary>
internal static class TestHttpContext
{
    public static IHttpContextAccessor Empty() => new HttpContextAccessor { HttpContext = null };

    public static IHttpContextAccessor For(string displayName)
    {
        var context = new DefaultHttpContext();
        context.Items["notaflow.current-user"] =
            new Billing.Api.Application.CurrentUser(Guid.NewGuid(), displayName.ToLowerInvariant(), displayName);
        return new HttpContextAccessor { HttpContext = context };
    }
}
