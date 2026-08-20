using System.Security.Cryptography;
using System.Text;
using Billing.Api.Api;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Billing.Api.Application;

public sealed class InvoiceService(
    BillingDbContext db,
    IInventoryClient inventory,
    TimeProvider clock,
    IHttpContextAccessor httpContext)
{
    public async Task<Invoice> CreateAsync(CreateInvoiceRequest request, CancellationToken cancellationToken)
    {
        if (request.Items is not { Count: > 0 and <= 20 })
            throw new DomainValidationException("A nota deve conter entre 1 e 20 produtos.");
        if (request.Items.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0))
            throw new DomainValidationException("Produto e quantidade devem ser válidos.");

        var normalized = request.Items
            .GroupBy(x => x.ProductId)
            .Select(g => new CreateInvoiceItemRequest(g.Key, checked(g.Sum(x => x.Quantity))))
            .ToList();

        var products = new List<ProductSnapshot>(normalized.Count);
        foreach (var item in normalized)
        {
            var product = await inventory.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new ResourceNotFoundException("PRODUCT_NOT_FOUND", $"O produto {item.ProductId} não foi encontrado.");
            products.Add(new(product.Id, product.Code, product.Description, item.Quantity));
        }

        var invoice = Invoice.Create(products, clock, httpContext.ActingUserName());
        db.Invoices.Add(invoice);
        await db.SaveChangesAsync(cancellationToken);
        return invoice;
    }

    /// <summary>
    /// Troca integralmente os itens de uma nota ainda aberta. Uma única versão
    /// evita que duas telas misturem alterações parciais antes do fechamento.
    /// </summary>
    public async Task<Invoice> UpdateAsync(
        Guid invoiceId,
        UpdateInvoiceRequest request,
        Guid expectedVersion,
        CancellationToken cancellationToken)
    {
        if (request.Items is not { Count: > 0 and <= 20 })
            throw new DomainValidationException("A nota deve conter entre 1 e 20 produtos.");
        if (request.Items.Any(x => x.ProductId == Guid.Empty || x.Quantity <= 0))
            throw new DomainValidationException("Produto e quantidade devem ser válidos.");

        var normalized = request.Items.GroupBy(x => x.ProductId)
            .Select(group => new CreateInvoiceItemRequest(group.Key, checked(group.Sum(x => x.Quantity)))).ToList();
        var snapshots = new List<ProductSnapshot>(normalized.Count);
        foreach (var item in normalized)
        {
            var product = await inventory.GetProductAsync(item.ProductId, cancellationToken)
                ?? throw new ResourceNotFoundException("PRODUCT_NOT_FOUND", $"O produto {item.ProductId} não foi encontrado.");
            snapshots.Add(new ProductSnapshot(product.Id, product.Code, product.Description, item.Quantity));
        }

        var invoice = await db.Invoices.AsNoTracking().Include(x => x.Items).Include(x => x.ClosureAttempts)
            .SingleOrDefaultAsync(x => x.Id == invoiceId, cancellationToken)
            ?? throw new ResourceNotFoundException("INVOICE_NOT_FOUND", "Nota não encontrada.");
        if (invoice.Status != InvoiceStatus.Open)
            throw new ConflictException("INVOICE_NOT_OPEN", "Somente notas abertas podem ser editadas.");
        if (invoice.ClosureAttempts.Any(attempt => attempt.State == ClosureAttemptState.Pending))
            throw new ConflictException("INVOICE_CLOSURE_PENDING", "A nota está em fechamento e não pode ser editada.");
        if (invoice.Version != expectedVersion)
            throw new ConflictException("INVOICE_VERSION_CONFLICT", "A nota foi alterada por outra pessoa. Atualize os dados antes de salvar.");

        // O provedor InMemory dos testes não implementa ExecuteUpdate. A
        // regra de concorrência já foi comparada acima; o caminho relacional
        // abaixo continua sendo o usado por PostgreSQL em produção.
        if (!db.Database.IsRelational())
        {
            var tracked = await db.Invoices.Include(x => x.Items).Include(x => x.ClosureAttempts)
                .Include(x => x.AuditEvents).SingleAsync(x => x.Id == invoiceId, cancellationToken);
            db.InvoiceItems.RemoveRange(tracked.Items);
            await db.SaveChangesAsync(cancellationToken);
            tracked.Items.Clear();
            tracked.ReplaceItems(snapshots, expectedVersion, clock, httpContext.ActingUserName());
            tracked.AuditEvents.Add(InvoiceAuditEvent.Create(tracked.Id, "Edited", tracked.UpdatedBy, tracked.UpdatedAt));
            await db.SaveChangesAsync(cancellationToken);
            return tracked;
        }

        var now = clock.GetUtcNow();
        var actor = httpContext.ActingUserName();
        var nextVersion = Guid.NewGuid();
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;
        var updated = await db.Invoices
            .Where(candidate => candidate.Id == invoiceId && candidate.Status == InvoiceStatus.Open && candidate.Version == expectedVersion)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(candidate => candidate.UpdatedAt, now)
                .SetProperty(candidate => candidate.UpdatedBy, actor)
                .SetProperty(candidate => candidate.Version, nextVersion), cancellationToken);
        if (updated != 1)
            throw new ConflictException("INVOICE_VERSION_CONFLICT", "A nota foi alterada por outra pessoa. Atualize os dados antes de salvar.");

        await db.InvoiceItems.Where(item => item.InvoiceId == invoiceId).ExecuteDeleteAsync(cancellationToken);
        db.InvoiceItems.AddRange(snapshots.Select(snapshot => InvoiceItem.Create(invoiceId, snapshot)));
        db.InvoiceAuditEvents.Add(InvoiceAuditEvent.Create(invoiceId, "Edited", actor, now));
        await db.SaveChangesAsync(cancellationToken);
        if (transaction is not null)
            await transaction.CommitAsync(cancellationToken);

        return await db.Invoices.AsNoTracking().Include(x => x.Items).Include(x => x.ClosureAttempts)
            .Include(x => x.AuditEvents).AsSplitQuery().SingleAsync(x => x.Id == invoiceId, cancellationToken);
    }

    public async Task<(Invoice Invoice, InvoiceClosureAttempt Attempt)> BeginClosureAsync(Guid invoiceId, CancellationToken cancellationToken)
    {
        var invoice = await db.Invoices
            .Include(x => x.Items)
            .Include(x => x.ClosureAttempts)
            .SingleOrDefaultAsync(x => x.Id == invoiceId, cancellationToken)
            ?? throw new ResourceNotFoundException("INVOICE_NOT_FOUND", "Nota não encontrada.");

        if (invoice.Status == InvoiceStatus.Closed)
        {
            var completed = invoice.ClosureAttempts
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault(x => x.State == ClosureAttemptState.Completed);
            if (completed is not null) return (invoice, completed);
            throw new ConflictException("INVOICE_ALREADY_CLOSED", "A nota já está fechada.");
        }

        var active = invoice.ClosureAttempts.SingleOrDefault(x => x.State == ClosureAttemptState.Pending);
        if (active is not null) return (invoice, active);

        var hash = ComputePayloadHash(invoice.Items);
        var attempt = InvoiceClosureAttempt.Start(invoice.Id, hash, clock.GetUtcNow());
        db.ClosureAttempts.Add(attempt);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return (invoice, attempt);
        }
        catch (DbUpdateException)
        {
            db.ChangeTracker.Clear();
            invoice = await db.Invoices
                .Include(x => x.Items)
                .Include(x => x.ClosureAttempts)
                .SingleAsync(x => x.Id == invoiceId, cancellationToken);
            var concurrentAttempt = invoice.ClosureAttempts
                .OrderByDescending(x => x.CreatedAt)
                .FirstOrDefault();
            if (concurrentAttempt is null) throw;
            return (invoice, concurrentAttempt);
        }
    }

    public static string ComputePayloadHash(IEnumerable<InvoiceItem> items)
    {
        var canonical = string.Join("|", items.OrderBy(x => x.ProductId).Select(x => $"{x.ProductId:N}:{x.Quantity}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical))).ToLowerInvariant();
    }
}
