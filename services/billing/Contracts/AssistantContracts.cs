namespace Billing.Api.Contracts;

/// <summary>Um turno já ocorrido, devolvido pelo cliente para dar contexto.</summary>
public sealed record AssistantTurn(string Role, string Text);

/// <summary>
/// Onde a pessoa está quando pergunta. Serve para o assistente entender
/// "quantos desse eu tenho?" sem exigir que ela repita o código.
/// <para>
/// É DADO, e o servidor trata como tal: o rótulo da tela é normalizado contra
/// uma lista fechada e o id só passa se for UUID. Texto livre vindo do cliente
/// entraria no prompt sem controle nenhum.
/// </para>
/// </summary>
public sealed record AssistantScreen(string? Route, string? EntityId);

/// <summary>
/// Pedido ao assistente: a mensagem nova, os turnos anteriores e a tela aberta.
/// A conversa é mantida pelo cliente e reenviada — o servidor não guarda sessão,
/// do mesmo jeito que a Messages API não guarda.
/// </summary>
public sealed record AssistantMessageRequest(
    string? Text,
    IReadOnlyList<AssistantTurn>? History,
    AssistantScreen? Screen);

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
