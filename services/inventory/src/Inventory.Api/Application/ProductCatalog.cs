using Inventory.Api.Contracts;
using Inventory.Api.Domain;
using Inventory.Api.Errors;
using Inventory.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Inventory.Api.Application;

public interface IProductCatalog
{
    Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);
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

public sealed class ProductCatalog(
    InventoryDbContext database,
    TimeProvider timeProvider) : IProductCatalog
{
    public async Task<ProductResponse> CreateAsync(
        CreateProductRequest request,
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
                request.TracksStock);
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

    public Task<ProductResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
        database.Products
            .AsNoTracking()
            .Where(product => product.Id == id)
            .Select(product => new ProductResponse(
                product.Id,
                product.Code,
                product.Description,
                product.Balance,
                product.TracksStock,
                product.CreatedAt))
            .SingleOrDefaultAsync(cancellationToken);

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
                product.CreatedAt))
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
            product.CreatedAt);

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
