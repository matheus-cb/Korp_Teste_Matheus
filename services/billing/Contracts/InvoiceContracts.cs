using System.Text.Json;
using Billing.Api.Domain;

namespace Billing.Api.Contracts;

public sealed record CreateInvoiceRequest(IReadOnlyList<CreateInvoiceItemRequest>? Items);
public sealed record CreateInvoiceItemRequest(Guid ProductId, int Quantity);
public sealed record UpdateInvoiceRequest(IReadOnlyList<CreateInvoiceItemRequest>? Items);

public sealed record InvoiceItemResponse(
    Guid ProductId,
    string Code,
    string Description,
    int Quantity);

public sealed record InvoiceAuditEventResponse(string Type, string ActorName, DateTimeOffset OccurredAt);

public sealed record ClosureAttemptResponse(
    Guid AttemptId,
    string State,
    string? ErrorCode,
    string? ErrorMessage,
    int RetryCount,
    DateTimeOffset UpdatedAt,
    IReadOnlyList<IgnoredItemResponse>? IgnoredItems = null);

/// <summary>Item que entrou na nota sem movimentar estoque (INV-04).</summary>
public sealed record IgnoredItemResponse(
    Guid ProductId,
    string Code,
    int Quantity,
    string Reason,
    string Message);

public sealed record InvoiceResponse(
    Guid Id,
    long Number,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    string CreatedBy,
    string? ClosedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    Guid Version,
    IReadOnlyList<InvoiceItemResponse> Items,
    ClosureAttemptResponse? Closure,
    IReadOnlyList<InvoiceAuditEventResponse> AuditEvents);

public sealed record InvoiceSummaryResponse(
    Guid Id,
    long Number,
    string Status,
    int ItemCount,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt,
    string CreatedBy,
    string? ClosedBy,
    DateTimeOffset UpdatedAt,
    string UpdatedBy,
    ClosureAttemptResponse? Closure);

public sealed record PagedResponse<T>(IReadOnlyList<T> Items, int Page, int PageSize, int Total);

public static class InvoiceMappings
{
    public static InvoiceResponse ToResponse(this Invoice invoice)
    {
        var attempt = invoice.ClosureAttempts.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
        return new(
            invoice.Id,
            invoice.Number,
            invoice.Status.ToString(),
            invoice.CreatedAt,
            invoice.ClosedAt,
            invoice.CreatedBy,
            invoice.ClosedBy,
            invoice.UpdatedAt,
            invoice.UpdatedBy,
            invoice.Version,
            invoice.Items.Select(x => new InvoiceItemResponse(
                x.ProductId, x.ProductCode, x.ProductDescription, x.Quantity)).ToList(),
            attempt?.ToResponse(),
            invoice.AuditEvents.OrderByDescending(x => x.OccurredAt)
                .Select(x => new InvoiceAuditEventResponse(x.Type, x.ActorName, x.OccurredAt)).ToList());
    }

    public static ClosureAttemptResponse ToResponse(this InvoiceClosureAttempt attempt) => new(
        attempt.Id,
        attempt.State.ToString(),
        attempt.ErrorCode,
        attempt.ErrorMessage,
        attempt.RetryCount,
        attempt.UpdatedAt,
        ReadIgnoredItems(attempt.IgnoredItemsJson));

    private static readonly JsonSerializerOptions IgnoredItemsOptions =
        new(JsonSerializerDefaults.Web);

    private static List<IgnoredItemResponse>? ReadIgnoredItems(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<List<IgnoredItemResponse>>(json, IgnoredItemsOptions);
}

/// <summary>Erro de uma linha do CSV; o arquivo continua sendo processado.</summary>
public sealed record ImportRowError(string? Reference, int Line, string Code, string Message);

public sealed record ImportResultResponse(
    Guid ImportId,
    int CreatedInvoices,
    int ErrorCount,
    IReadOnlyList<ImportRowError> Errors,
    bool AlreadyImported,
    string? Message);

/// <summary>Tipo de ação que o assistente pode propor. Nunca executa sozinho.</summary>
public enum ProposedActionKind
{
    CreateInvoice = 0,
    CreateAndCloseInvoice = 1,
    CreateProduct = 2,
}

public sealed record ProposedItem(Guid ProductId, string Code, string Description, int Quantity);

/// <summary>
/// Produto que o assistente propõe cadastrar. Diferente de <see cref="ProposedItem"/>,
/// aqui não há proveniência MCP a verificar — o produto ainda não existe. A defesa
/// é outra: formato validado no servidor e confirmação humana antes de criar.
/// </summary>
public sealed record ProposedProduct(string Code, string Description, int Balance, bool TracksStock);

public sealed record ProposedActionResponse(
    string Kind,
    IReadOnlyList<ProposedItem> Items,
    IReadOnlyList<ProposedProduct> Products,
    DateTimeOffset ExpiresAt,
    /// <summary>Assinado pelo servidor: sem ele a execução é recusada.</summary>
    string Token);

public sealed record ConfirmActionRequest(string Token);

public sealed record ProposedActionResultResponse(
    Guid InvoiceId,
    long Number,
    string Status,
    bool Closed,
    string ConfirmedBy);
