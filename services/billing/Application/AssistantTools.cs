using System.Text.Json;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Billing.Api.Application;

/// <summary>
/// Ferramentas de leitura sobre o domínio do próprio Billing.
/// <para>
/// Elas não são tools MCP de propósito. O servidor MCP é do Inventory e nota é
/// domínio do Billing, que é o cliente MCP — uma tool de nota ali faria o
/// Billing chamar a si mesmo pelo protocolo (INV-27). Aqui o assistente as
/// alcança direto, e elas continuam somente leitura: nada nesta classe escreve.
/// </para>
/// </summary>
public interface IAssistantLocalTools
{
    IReadOnlyList<AiToolDefinition> Tools { get; }

    Task<AiToolResult> CallAsync(string name, JsonElement arguments, CancellationToken cancellationToken);

    bool Owns(string name);
}

public sealed class AssistantLocalTools(BillingDbContext db) : IAssistantLocalTools
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static readonly JsonElement ListInvoicesSchema = Parse("""
        {"type":"object","properties":{
          "page":{"type":"integer","description":"Page number, starting at 1."},
          "pageSize":{"type":"integer","description":"Items per page; 1 to 20."},
          "status":{"type":"string","description":"Optional filter: Open or Closed."}
        },"additionalProperties":false}
        """);

    private static readonly JsonElement GetInvoiceSchema = Parse("""
        {"type":"object","properties":{
          "invoiceId":{"type":"string","description":"Invoice UUID returned by list_invoices."}
        },"required":["invoiceId"],"additionalProperties":false}
        """);

    public IReadOnlyList<AiToolDefinition> Tools { get; } =
    [
        new("list_invoices",
            "List invoices of this system, newest first, with number, status, item count and who created. Read-only.",
            ListInvoicesSchema),
        new("get_invoice",
            "Get one invoice by UUID, with its items. Read-only.",
            GetInvoiceSchema)
    ];

    public bool Owns(string name) => name is "list_invoices" or "get_invoice";

    public async Task<AiToolResult> CallAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
    {
        return name switch
        {
            "list_invoices" => await ListAsync(arguments, cancellationToken),
            "get_invoice" => await GetAsync(arguments, cancellationToken),
            _ => throw new InvalidOperationException($"Tool '{name}' is not a local assistant tool.")
        };
    }

    private async Task<AiToolResult> ListAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var page = Math.Max(GetInt(arguments, "page", 1), 1);
        var pageSize = Math.Clamp(GetInt(arguments, "pageSize", 10), 1, 20);
        var status = GetString(arguments, "status");

        var query = db.Invoices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status) &&
            Enum.TryParse<InvoiceStatus>(status, ignoreCase: true, out var parsed))
        {
            query = query.Where(invoice => invoice.Status == parsed);
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(invoice => invoice.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(invoice => new
            {
                invoiceId = invoice.Id.ToString(),
                number = invoice.Number,
                status = invoice.Status.ToString(),
                itemCount = invoice.Items.Count,
                createdBy = invoice.CreatedBy,
                closedBy = invoice.ClosedBy,
                createdAt = invoice.CreatedAt
            })
            .ToListAsync(cancellationToken);

        return Ok(new { invoices = items, page, pageSize, total });
    }

    private async Task<AiToolResult> GetAsync(JsonElement arguments, CancellationToken cancellationToken)
    {
        var raw = GetString(arguments, "invoiceId");
        if (!Guid.TryParse(raw, out var id) || id == Guid.Empty)
            return Ok(new { errorCode = "VALIDATION_ERROR", errorMessage = "invoiceId must be a non-empty UUID." });

        var invoice = await db.Invoices
            .AsNoTracking()
            .Where(candidate => candidate.Id == id)
            .Select(candidate => new
            {
                invoiceId = candidate.Id.ToString(),
                number = candidate.Number,
                status = candidate.Status.ToString(),
                createdBy = candidate.CreatedBy,
                closedBy = candidate.ClosedBy,
                createdAt = candidate.CreatedAt,
                items = candidate.Items.Select(item => new
                {
                    productId = item.ProductId.ToString(),
                    code = item.ProductCode,
                    description = item.ProductDescription,
                    quantity = item.Quantity
                })
            })
            .SingleOrDefaultAsync(cancellationToken);

        return invoice is null
            ? Ok(new { errorCode = "INVOICE_NOT_FOUND", errorMessage = "Invoice was not found." })
            : Ok(invoice);
    }

    private static AiToolResult Ok(object payload) =>
        new(JsonSerializer.SerializeToElement(payload, Json), false);

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    private static int GetInt(JsonElement element, string name, int fallback) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number &&
        value.TryGetInt32(out var parsed)
            ? parsed
            : fallback;

    private static string? GetString(JsonElement element, string name) =>
        element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
