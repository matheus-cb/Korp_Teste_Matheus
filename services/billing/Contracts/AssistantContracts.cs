namespace Billing.Api.Contracts;

/// <summary>Um turno já ocorrido, devolvido pelo cliente para dar contexto.</summary>
public sealed record AssistantTurn(string Role, string Text);

/// <summary>
/// Pedido ao assistente: a mensagem nova mais os turnos anteriores. A conversa
/// é mantida pelo cliente e reenviada — o servidor não guarda sessão, do mesmo
/// jeito que a Messages API não guarda.
/// </summary>
public sealed record AssistantMessageRequest(
    string? Text,
    IReadOnlyList<AssistantTurn>? History);

/// <summary>
/// Resposta do assistente. Três desfechos possíveis, e eles não são exclusivos
/// entre si apenas por construção do prompt: texto sempre existe; rascunho
/// aparece quando ele montou uma lista de itens; ação aparece quando ele propõe
/// criar a nota — e aí depende de confirmação humana (INV-24).
/// </summary>
public sealed record AssistantMessageResponse(
    Guid RunId,
    string Text,
    IReadOnlyList<AiDraftItem> Items,
    IReadOnlyList<UnresolvedDraftItem> UnresolvedItems,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<AiDraftStep> Steps,
    ProposedActionResponse? Action);
