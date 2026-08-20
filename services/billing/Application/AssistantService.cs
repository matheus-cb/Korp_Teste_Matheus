using System.Diagnostics;
using System.Text.Json;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Billing.Api.Options;
using Microsoft.Extensions.Options;

namespace Billing.Api.Application;

/// <summary>Um turno da conversa, como o cliente o reenvia.</summary>
public sealed record AssistantHistoryTurn(bool FromUser, string Text);

public sealed record AssistantClientRequest(
    string Text,
    IReadOnlyList<AssistantHistoryTurn> History,
    string? Screen);

/// <summary>
/// Resposta do provedor. <see cref="ProposesInvoice"/> distingue "montei um
/// rascunho para você olhar" de "quero criar a nota" — só o segundo produz uma
/// ação assinada, e mesmo assim a execução depende de confirmação humana.
/// </summary>
public sealed record AssistantClientReply(
    string Text,
    bool ProposesInvoice,
    IReadOnlyList<ProposedProduct>? ProposedProducts,
    IReadOnlyList<AiDraftModelItem> Items,
    IReadOnlyList<UnresolvedDraftItem> UnresolvedItems,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<AiDraftStep> Steps,
    IReadOnlyList<string> ToolNames,
    int InputTokens,
    int OutputTokens);

public interface IAssistantClient
{
    bool IsConfigured { get; }
    string ModelName { get; }
    Task<AssistantClientReply> RespondAsync(AssistantClientRequest request, CancellationToken cancellationToken);
}

/// <summary>
/// Assistente conversacional. Diferente do <see cref="AiDraftService"/>, que
/// responde a um pedido isolado, aqui há histórico e o desfecho pode ser uma
/// ação proposta.
/// </summary>
public sealed class AssistantService(
    BillingDbContext db,
    IAssistantClient client,
    ProposedActionService actions,
    IOptions<OpenAiOptions> options,
    TimeProvider clock,
    ILogger<AssistantService> logger)
{
    /// <summary>
    /// Telas conhecidas. Lista fechada de proposito: o cliente nao escolhe o
    /// texto que entra no prompt, so aponta qual das telas conhecidas esta aberta.
    /// </summary>
    private static readonly Dictionary<string, string> Telas = new(StringComparer.OrdinalIgnoreCase)
    {
        ["visao-geral"] = "a visão geral",
        ["produtos"] = "a lista de produtos",
        ["notas"] = "a lista de notas",
        ["nota"] = "uma nota específica",
        ["movimentacoes"] = "o extrato de movimentações",
    };

    private const int MaxHistoryTurns = 10;
    private const int MaxTextLength = 2000;

    private static string? DescreverTela(AssistantScreen? screen)
    {
        if (screen?.Route is null || !Telas.TryGetValue(screen.Route, out var descricao))
            return null;

        // O id so entra se for UUID: qualquer outra coisa seria texto livre do
        // cliente indo direto para o prompt.
        return Guid.TryParse(screen.EntityId, out var id)
            ? $"{descricao} (id {id})"
            : descricao;
    }

    public async Task<AssistantMessageResponse> RespondAsync(
        AssistantMessageRequest request,
        CancellationToken cancellationToken)
    {
        var text = string.IsNullOrWhiteSpace(request.Text) ? null : request.Text.Trim();
        if (text is null)
            throw new DomainValidationException("Escreva uma mensagem para o assistente.");
        if (text.Length > MaxTextLength)
            throw new DomainValidationException($"A mensagem deve ter no máximo {MaxTextLength} caracteres.");

        if (!client.IsConfigured)
            throw new DependencyUnavailableException(
                "AI_DISABLED",
                "O assistente está desabilitado porque nenhum provedor de IA foi configurado.");

        // Só os turnos recentes viajam: a ponte tem teto de prompt, e histórico
        // antigo custa mais do que ajuda.
        var history = (request.History ?? [])
            .Where(turn => !string.IsNullOrWhiteSpace(turn.Text))
            .TakeLast(MaxHistoryTurns)
            .Select(turn => new AssistantHistoryTurn(
                !string.Equals(turn.Role, "assistant", StringComparison.OrdinalIgnoreCase),
                turn.Text.Length > MaxTextLength ? turn.Text[..MaxTextLength] : turn.Text))
            .ToList();

        var tela = DescreverTela(request.Screen);

        var run = AiDraftRun.Start(client.ModelName, options.Value.PromptVersion, clock.GetUtcNow());
        db.AiDraftRuns.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var reply = await client.RespondAsync(
                new AssistantClientRequest(text, history, tela),
                cancellationToken);

            ProposedActionResponse? action = null;
            if (reply.ProposedProducts is { Count: > 0 } produtos)
            {
                action = actions.ProposeProducts(produtos);
            }
            else if (reply.ProposesInvoice)
            {
                if (reply.Items.Count == 0)
                    throw new DomainValidationException("O assistente propôs uma nota sem itens.");

                // CreateInvoice, nunca CreateAndCloseInvoice: o assistente deixa a
                // nota pronta e aberta, e fechar continua sendo ato humano.
                action = actions.Propose(
                    ProposedActionKind.CreateInvoice,
                    reply.Items
                        .Select(item => new ProposedItem(item.ProductId, item.Code, item.Description, item.Quantity))
                        .ToList());
            }

            run.Complete(
                JsonSerializer.Serialize(reply.ToolNames.Distinct()),
                reply.InputTokens,
                reply.OutputTokens,
                0m,
                stopwatch.ElapsedMilliseconds,
                clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);

            return new AssistantMessageResponse(
                run.Id,
                reply.Text,
                reply.Items
                    .Select(item => new AiDraftItem(
                        item.ProductId, item.Code, item.Description, item.Quantity, item.Availability))
                    .ToList(),
                reply.UnresolvedItems,
                reply.Warnings,
                reply.Steps,
                action);
        }
        catch (Exception exception) when (exception is not DomainValidationException)
        {
            // INV-22: nem a mensagem do usuário nem a resposta do modelo entram no log.
            logger.LogWarning("A execução {RunId} do assistente falhou.", run.Id);
            run.Fail("ASSISTANT_FAILED", "[]", stopwatch.ElapsedMilliseconds, clock.GetUtcNow());
            await db.SaveChangesAsync(cancellationToken);
            throw;
        }
    }
}

/// <summary>
/// Provedor de conversa ausente. Existe para que o endpoint do assistente
/// responda AI_DISABLED de forma limpa quando o provedor configurado só sabe
/// montar rascunho (INV-23), em vez de a rota falhar por falta de registro.
/// </summary>
public sealed class UnavailableAssistantClient : IAssistantClient
{
    public bool IsConfigured => false;

    public string ModelName => "indisponivel";

    public Task<AssistantClientReply> RespondAsync(
        AssistantClientRequest request,
        CancellationToken cancellationToken) =>
        throw new DependencyUnavailableException(
            "AI_DISABLED",
            "O provedor de IA configurado não oferece o assistente conversacional.");
}
