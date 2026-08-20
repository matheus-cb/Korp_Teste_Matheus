using Billing.Api.Application;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Billing.Api.Api;

public static class InvoiceEndpoints
{
    public static IEndpointRouteBuilder MapInvoiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/invoices").WithTags("Invoices");

        group.MapPost("/", CreateAsync).Produces<InvoiceResponse>(StatusCodes.Status201Created);
        group.MapPut("/{id:guid}", UpdateAsync).Produces<InvoiceResponse>().ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/", ListAsync).Produces<PagedResponse<InvoiceSummaryResponse>>();
        group.MapGet("/{id:guid}", GetAsync).Produces<InvoiceResponse>().ProducesProblem(StatusCodes.Status404NotFound);
        group.MapPost("/{id:guid}/close", CloseAsync)
            .Produces<InvoiceResponse>()
            .Produces<InvoiceResponse>(StatusCodes.Status202Accepted)
            .ProducesProblem(StatusCodes.Status409Conflict);
        group.MapGet("/{id:guid}/pdf", PdfAsync).Produces(StatusCodes.Status200OK, contentType: "application/pdf");
        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateInvoiceRequest request,
        InvoiceService service,
        CancellationToken cancellationToken)
    {
        var invoice = await service.CreateAsync(request, cancellationToken);
        return Results.Created($"/api/invoices/{invoice.Id}", invoice.ToResponse());
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateInvoiceRequest request,
        HttpRequest httpRequest,
        InvoiceService service,
        CancellationToken cancellationToken)
    {
        var rawVersion = httpRequest.Headers["If-Match"].FirstOrDefault()?.Trim().Trim('"');
        if (!Guid.TryParse(rawVersion, out var version))
            throw new ConflictException("INVOICE_VERSION_REQUIRED", "Atualize a nota antes de salvar para evitar sobrescrever alterações de outra pessoa.");

        var invoice = await service.UpdateAsync(id, request, version, cancellationToken);
        return Results.Ok(invoice.ToResponse());
    }

    private static async Task<IResult> ListAsync(
        int? page,
        int? pageSize,
        string? status,
        BillingDbContext db,
        CancellationToken cancellationToken)
    {
        var requestedPage = Math.Max(1, page ?? 1);
        var requestedPageSize = Math.Clamp(pageSize ?? 20, 1, 100);
        var query = db.Invoices.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!Enum.TryParse<InvoiceStatus>(status, true, out var parsedStatus))
                throw new DomainValidationException("Status deve ser Open ou Closed.");
            query = query.Where(x => x.Status == parsedStatus);
        }

        var total = await query.CountAsync(cancellationToken);
        var invoices = await query
            .Include(x => x.Items)
            .Include(x => x.ClosureAttempts)
            .AsSplitQuery()
            .OrderByDescending(x => x.Number)
            .Skip((requestedPage - 1) * requestedPageSize)
            .Take(requestedPageSize)
            .ToListAsync(cancellationToken);
        var items = invoices.Select(invoice =>
        {
            var attempt = invoice.ClosureAttempts.OrderByDescending(x => x.CreatedAt).FirstOrDefault();
            return new InvoiceSummaryResponse(
                invoice.Id,
                invoice.Number,
                invoice.Status.ToString(),
                invoice.Items.Count,
                invoice.CreatedAt,
                invoice.ClosedAt,
                invoice.CreatedBy,
                invoice.ClosedBy,
                invoice.UpdatedAt,
                invoice.UpdatedBy,
                attempt?.ToResponse());
        }).ToList();
        return Results.Ok(new PagedResponse<InvoiceSummaryResponse>(items, requestedPage, requestedPageSize, total));
    }

    private static async Task<IResult> GetAsync(Guid id, BillingDbContext db, CancellationToken cancellationToken)
    {
        var invoice = await LoadInvoiceAsync(db, id, cancellationToken);
        return Results.Ok(invoice.ToResponse());
    }

    private static async Task<IResult> CloseAsync(
        Guid id,
        InvoiceService service,
        ClosureCoordinator coordinator,
        BillingDbContext db,
        CancellationToken cancellationToken)
    {
        var (_, attempt) = await service.BeginClosureAsync(id, cancellationToken);
        var result = await coordinator.ProcessAsync(attempt.Id, true, cancellationToken);
        db.ChangeTracker.Clear();
        var invoice = await LoadInvoiceAsync(db, id, cancellationToken);

        if (result.State == ClosureAttemptState.Rejected)
            throw new ConflictException(result.ErrorCode ?? "INVENTORY_REJECTED", result.ErrorMessage ?? "O estoque rejeitou o fechamento.");
        if (result.State == ClosureAttemptState.Pending)
            return Results.Accepted($"/api/invoices/{id}", invoice.ToResponse());
        return Results.Ok(invoice.ToResponse());
    }

    private static async Task<IResult> PdfAsync(
        Guid id,
        BillingDbContext db,
        IInvoicePdfGenerator generator,
        CancellationToken cancellationToken)
    {
        var invoice = await LoadInvoiceAsync(db, id, cancellationToken);
        var bytes = generator.Generate(invoice);
        return Results.File(bytes, "application/pdf", $"nota-{invoice.Number}.pdf");
    }

    private static async Task<Invoice> LoadInvoiceAsync(BillingDbContext db, Guid id, CancellationToken cancellationToken) =>
        await db.Invoices.AsNoTracking()
            .Include(x => x.Items)
            .Include(x => x.ClosureAttempts)
            .Include(x => x.AuditEvents)
            .AsSplitQuery()
            .SingleOrDefaultAsync(x => x.Id == id, cancellationToken)
        ?? throw new ResourceNotFoundException("INVOICE_NOT_FOUND", "Nota não encontrada.");
}
