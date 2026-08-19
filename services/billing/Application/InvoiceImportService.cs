using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Billing.Api.Api;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Billing.Api.Application;

/// <summary>
/// Importação de notas por CSV.
///
/// Formato: uma linha por item, agrupadas pela coluna `nota`. Aceita `;` e `,`
/// como separador, porque planilha brasileira exporta em ponto e vírgula.
///
/// <code>
/// nota;codigo;quantidade
/// 1;CAB-USBC-2M;2
/// 1;TEC-SF-01;1
/// 2;MON-24-IPS;3
/// </code>
///
/// Falha parcial é o caso esperado: uma linha inválida vira erro daquela linha
/// e o resto do arquivo continua.
/// </summary>
public sealed class InvoiceImportService(
    BillingDbContext db,
    IInventoryClient inventory,
    InvoiceService invoices,
    ClosureCoordinator closures,
    TimeProvider clock,
    IHttpContextAccessor httpContext)
{
    private const int MaxLines = 2_000;

    public async Task<ImportResultResponse> ImportAsync(
        Stream csv,
        string fileName,
        bool closeAfterImport,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(csv, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        var content = await reader.ReadToEndAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new DomainValidationException("O arquivo está vazio.");
        }

        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));

        // Idempotência por conteúdo: reenviar o mesmo arquivo não duplica notas.
        var previous = await db.Imports.SingleOrDefaultAsync(
            record => record.ContentHash == hash,
            cancellationToken);
        if (previous is not null)
        {
            return new ImportResultResponse(
                previous.Id,
                previous.CreatedInvoices,
                0,
                [],
                true,
                "Este arquivo já havia sido importado; nada foi duplicado.");
        }

        var (groups, errors) = Parse(content);
        await ResolveCodesAsync(groups, errors, cancellationToken);

        var created = 0;
        foreach (var group in groups.Where(candidate => candidate.Items.Count > 0))
        {
            try
            {
                var invoice = await invoices.CreateAsync(
                    new CreateInvoiceRequest(
                        group.Items
                            .Select(item => new CreateInvoiceItemRequest(item.ProductId, item.Quantity))
                            .ToList()),
                    cancellationToken);
                created++;

                if (closeAfterImport)
                {
                    // Reaproveita a saga: mesma tentativa, mesma chave idempotente.
                    var (_, attempt) = await invoices.BeginClosureAsync(invoice.Id, cancellationToken);
                    await closures.ProcessAsync(attempt.Id, true, cancellationToken);
                }
            }
            catch (AppException exception)
            {
                errors.Add(new ImportRowError(group.Reference, 0, exception.Code, exception.Message));
            }
        }

        var record = InvoiceImport.Create(
            fileName,
            hash,
            created,
            clock.GetUtcNow(),
            httpContext.ActingUserName());
        db.Imports.Add(record);
        await db.SaveChangesAsync(cancellationToken);

        return new ImportResultResponse(record.Id, created, errors.Count, errors, false, null);
    }

    private static (List<ImportGroup> Groups, List<ImportRowError> Errors) Parse(string content)
    {
        var errors = new List<ImportRowError>();
        var groups = new Dictionary<string, ImportGroup>(StringComparer.OrdinalIgnoreCase);

        var lines = content
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        if (lines.Length > MaxLines)
        {
            throw new DomainValidationException($"O arquivo excede o limite de {MaxLines} linhas.");
        }

        var start = LooksLikeHeader(lines[0]) ? 1 : 0;
        for (var index = start; index < lines.Length; index++)
        {
            var lineNumber = index + 1;
            var raw = lines[index].Trim().TrimStart('﻿');
            if (raw.Length == 0) continue;

            var separator = raw.Contains(';', StringComparison.Ordinal) ? ';' : ',';
            var parts = raw.Split(separator);
            if (parts.Length < 3)
            {
                errors.Add(new ImportRowError(null, lineNumber, "CSV_MALFORMED_LINE",
                    "Esperado: nota, código do produto e quantidade."));
                continue;
            }

            var reference = parts[0].Trim();
            var code = parts[1].Trim();
            if (reference.Length == 0 || code.Length == 0)
            {
                errors.Add(new ImportRowError(reference, lineNumber, "CSV_MISSING_FIELD",
                    "Nota e código do produto são obrigatórios."));
                continue;
            }

            if (!int.TryParse(parts[2].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var quantity)
                || quantity <= 0)
            {
                errors.Add(new ImportRowError(reference, lineNumber, "CSV_INVALID_QUANTITY",
                    "Quantidade deve ser um inteiro positivo."));
                continue;
            }

            if (!groups.TryGetValue(reference, out var group))
            {
                group = new ImportGroup(reference);
                groups[reference] = group;
            }

            group.Pending.Add(new PendingItem(lineNumber, code, quantity));
        }

        return ([.. groups.Values], errors);
    }

    /// <summary>
    /// Resolve os códigos contra o catálogo. Código inexistente vira erro
    /// daquela linha, não do arquivo inteiro.
    /// </summary>
    private async Task ResolveCodesAsync(
        List<ImportGroup> groups,
        List<ImportRowError> errors,
        CancellationToken cancellationToken)
    {
        var cache = new Dictionary<string, InventoryProduct?>(StringComparer.OrdinalIgnoreCase);

        foreach (var group in groups)
        {
            foreach (var pending in group.Pending)
            {
                if (!cache.TryGetValue(pending.Code, out var product))
                {
                    product = await inventory.FindByCodeAsync(pending.Code, cancellationToken);
                    cache[pending.Code] = product;
                }

                if (product is null)
                {
                    errors.Add(new ImportRowError(group.Reference, pending.Line, "PRODUCT_NOT_FOUND",
                        $"Produto {pending.Code} não existe no catálogo."));
                    continue;
                }

                group.Items.Add(new ResolvedItem(product.Id, pending.Quantity));
            }
        }
    }

    private static bool LooksLikeHeader(string line) =>
        line.Contains("nota", StringComparison.OrdinalIgnoreCase) &&
        line.Contains("quantidade", StringComparison.OrdinalIgnoreCase);

    private sealed record PendingItem(int Line, string Code, int Quantity);

    private sealed record ResolvedItem(Guid ProductId, int Quantity);

    private sealed class ImportGroup(string reference)
    {
        public string Reference { get; } = reference;
        public List<PendingItem> Pending { get; } = [];
        public List<ResolvedItem> Items { get; } = [];
    }
}
