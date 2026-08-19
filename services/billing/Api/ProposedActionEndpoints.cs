using Billing.Api.Application;
using Billing.Api.Contracts;
using Microsoft.AspNetCore.Mvc;

namespace Billing.Api.Api;

/// <summary>
/// Confirmação de ação proposta pelo assistente. A proposta em si nasce no
/// fluxo de rascunho; aqui só se executa o que o operador confirmou, e a
/// validação da assinatura acontece no servidor.
/// </summary>
public static class ProposedActionEndpoints
{
    public static IEndpointRouteBuilder MapProposedActionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/api/assistant/actions/confirm", ConfirmAsync)
            .Produces<ProposedActionResultResponse>()
            .ProducesProblem(StatusCodes.Status400BadRequest)
            .ProducesProblem(StatusCodes.Status409Conflict)
            .WithTags("Assistant");

        return endpoints;
    }

    private static async Task<IResult> ConfirmAsync(
        [FromBody] ConfirmActionRequest request,
        ProposedActionService actions,
        CancellationToken cancellationToken)
    {
        var result = await actions.ConfirmAsync(request.Token, cancellationToken);
        return Results.Ok(result);
    }
}
