using Inventory.Api.Contracts;
using Inventory.Api.Domain;
using Inventory.Api.Errors;
using Inventory.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inventory.Api.Application;

/// <summary>
/// Consulta do catalogo. E este o contrato que as tools MCP recebem: sem metodo
/// de escrita, uma tool nova nao tem por onde alterar o catalogo, mesmo que
/// alguem esqueca a anotacao <c>ReadOnly</c> (INV-27). A anotacao continua
/// existindo para o protocolo; este tipo e o que torna a escrita inalcancavel.
/// </summary>
public interface IReadOnlyProductCatalog
{
    Task<ProductResponse?> GetAsync(Guid id, CancellationToken cancellationToken);
    Task<ProductPageResponse> SearchAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken);
    Task<IReadOnlyList<ProductAvailabilityResponse>> CheckAvailabilityAsync(
        IReadOnlyList<AvailabilityItemRequest> items,
        CancellationToken cancellationToken);
}

/// <summary>
/// Consulta mais escrita. So o caminho REST recebe este contrato; injeta-lo numa
/// tool MCP reabre exatamente o buraco que a separacao acima fecha.
/// </summary>
public interface IProductCatalog : IReadOnlyProductCatalog
{
    Task<ProductResponse> CreateAsync(CreateProductRequest request, string actorName, CancellationToken cancellationToken);
    Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, Guid expectedVersion, string actorName, CancellationToken cancellationToken);
}

public sealed class ProductCatalog(
    InventoryDbContext database,
    TimeProvider timeProvider) : IProductCatalog
{
    public async Task<ProductResponse> CreateAsync(
        CreateProductRequest request,
        string actorName,
        CancellationToken cancellationToken)
    {
        Product product;
        try
        {
            product = Product.Create(
                request.Code,
                request.Description,
                request.Balance,
                timeProvider.GetUtcNow(),
                request.TracksStock,
                actorName);
        }
        catch (ArgumentException exception)
        {
            throw InventoryApiException.BadRequest(ErrorCodes.ValidationError, exception.Message);
        }

        database.Products.Add(product);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception) when (IsDuplicateProductCode(exception))
        {
            throw InventoryApiException.Conflict(
                ErrorCodes.ProductCodeAlreadyExists,
                $"Ja existe um produto com o codigo {product.Code}.");
        }

        return ToResponse(product);
    }

    public async Task<ProductResponse> UpdateAsync(Guid id, UpdateProductRequest request, Guid expectedVersion, string actorName, CancellationToken cancellationToken)
    {
        var product = await database.Products
            .Include(product => product.AuditEvents)
            .SingleOrDefaultAsync(product => product.Id == id, cancellationToken)
            ?? throw InventoryApiException.NotFound(ErrorCodes.ProductNotFound, $"Product '{id}' was not found.");
        try
        {
            product.UpdateMetadata(request.Code, request.Description, request.TracksStock, expectedVersion, timeProvider.GetUtcNow(), actorName);
            // A entidade já existia no contexto. Registrar explicitamente o novo
            // evento evita que uma coleção carregada seja tratada como atualização
            // de um evento inexistente quando o change tracker detecta a inclusão.
            database.ProductAuditEvents.Add(product.AuditEvents[^1]);
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (InvalidOperationException exception) when (exception.Message == "PRODUCT_VERSION_CONFLICT")
        {
            throw InventoryApiException.Conflict("PRODUCT_VERSION_CONFLICT", "O produto foi alterado por outra pessoa. Atualize os dados antes de salvar.");
        }
        catch (InvalidOperationException exception) when (exception.Message == "PRODUCT_STOCK_CONTROL_REQUIRES_ZERO_BALANCE")
        {
            throw InventoryApiException.Conflict("PRODUCT_STOCK_CONTROL_REQUIRES_ZERO_BALANCE", "Zere o saldo por um ajuste auditável antes de desativar o controle de estoque.");
        }
        catch (ArgumentException exception)
        {
            throw InventoryApiException.BadRequest(ErrorCodes.ValidationError, exception.Message);
        }
        catch (DbUpdateException exception) when (IsDuplicateProductCode(exception))
        {
            throw InventoryApiException.Conflict(ErrorCodes.ProductCodeAlreadyExists, $"Ja existe um produto com o codigo {Product.NormalizeCode(request.Code)}.");
        }
        return ToResponse(product);
    }

    public async Task<ProductResponse?> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var product = await database.Products
            .AsNoTracking()
            .Include(product => product.AuditEvents)
            .SingleOrDefaultAsync(product => product.Id == id, cancellationToken);

        return product is null ? null : ToResponse(product);
    }

    public async Task<ProductPageResponse> SearchAsync(
        string? query,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, 100);

        IQueryable<Product> products = database.Products.AsNoTracking();
        if (!string.IsNullOrWhiteSpace(query))
        {
            var search = query.Trim();
            if (search.Length > 100)
            {
                throw InventoryApiException.BadRequest(
                    ErrorCodes.ValidationError,
                    "Product search query cannot exceed 100 characters.");
            }

            var pattern = $"%{EscapeLikePattern(search)}%";
            products = products.Where(product =>
                EF.Functions.ILike(product.Code, pattern, "\\") ||
                EF.Functions.ILike(product.Description, pattern, "\\"));
        }

        var totalCount = await products.CountAsync(cancellationToken);
        var items = await products
            .OrderBy(product => product.Code)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(product => new ProductResponse(
                product.Id,
                product.Code,
                product.Description,
                product.Balance,
                product.TracksStock,
                product.CreatedAt, product.CreatedBy, product.UpdatedAt, product.UpdatedBy, product.Version))
            .ToListAsync(cancellationToken);

        return new ProductPageResponse(items, page, pageSize, totalCount);
    }

    public async Task<IReadOnlyList<ProductAvailabilityResponse>> CheckAvailabilityAsync(
        IReadOnlyList<AvailabilityItemRequest> items,
        CancellationToken cancellationToken)
    {
        ValidateAvailabilityItems(items);

        var productIds = items.Select(item => item.ProductId).Distinct().ToArray();
        var products = await database.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        return items.Select(item =>
        {
            products.TryGetValue(item.ProductId, out var product);
            // Produto sem controle de estoque esta sempre disponivel: nao ha
            // saldo a consumir. A IA nao pode inventar isso — quem decide e aqui.
            return new ProductAvailabilityResponse(
                item.ProductId,
                product?.Code,
                product?.Description,
                item.Quantity,
                product?.Balance ?? 0,
                product is not null,
                product?.TracksStock ?? true,
                product is not null && product.CanFulfill(item.Quantity));
        }).ToArray();
    }

    private static void ValidateAvailabilityItems(IReadOnlyList<AvailabilityItemRequest> items)
    {
        if (items.Count is < 1 or > 20)
        {
            throw InventoryApiException.BadRequest(
                ErrorCodes.ValidationError,
                "Availability checks require between 1 and 20 items.");
        }

        if (items.Any(item => item.ProductId == Guid.Empty || item.Quantity <= 0))
        {
            throw InventoryApiException.BadRequest(
                ErrorCodes.ValidationError,
                "Each availability item requires a productId and a positive quantity.");
        }

        if (items.Select(item => item.ProductId).Distinct().Count() != items.Count)
        {
            throw InventoryApiException.BadRequest(
                ErrorCodes.ValidationError,
                "A product can appear only once in an availability check.");
        }
    }

    private static ProductResponse ToResponse(Product product) =>
        new(
            product.Id,
            product.Code,
            product.Description,
            product.Balance,
            product.TracksStock,
            product.CreatedAt, product.CreatedBy, product.UpdatedAt, product.UpdatedBy, product.Version,
            product.AuditEvents
                .OrderByDescending(auditEvent => auditEvent.OccurredAt)
                .Select(auditEvent => new ProductAuditEventResponse(
                    auditEvent.Type,
                    auditEvent.ActorName,
                    auditEvent.OccurredAt))
                .ToArray());

    private static string EscapeLikePattern(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);

    private static bool IsDuplicateProductCode(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: "UX_Products_Code"
        };
}
