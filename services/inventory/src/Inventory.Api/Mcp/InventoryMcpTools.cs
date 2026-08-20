using System.ComponentModel;
using Inventory.Api.Application;
using Inventory.Api.Contracts;
using ModelContextProtocol.Server;

namespace Inventory.Api.Mcp;

[McpServerToolType]
public sealed class InventoryMcpTools
{
    [McpServerTool(
        Name = "search_products",
        Title = "Search products",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false,
        Idempotent = true,
        UseStructuredContent = true)]
    [Description("Search the internal product catalog by code or description. Returns at most five current catalog matches and never changes inventory.")]
    public static async Task<SearchProductsToolResult> SearchProducts(
        IReadOnlyProductCatalog catalog,
        [Description("Text to match against product code or description; 1 to 100 characters.")] string query,
        [Description("Maximum number of matches to return; from 1 to 5.")] int limit = 5,
        CancellationToken cancellationToken = default)
    {
        query = query?.Trim() ?? string.Empty;
        if (query.Length is < 1 or > 100)
        {
            return new SearchProductsToolResult(
                [],
                "VALIDATION_ERROR",
                "query must contain between 1 and 100 characters.");
        }

        if (limit is < 1 or > 5)
        {
            return new SearchProductsToolResult(
                [],
                "VALIDATION_ERROR",
                "limit must be between 1 and 5.");
        }

        var result = await catalog.SearchAsync(query, 1, limit, cancellationToken);
        return new SearchProductsToolResult(
            result.Items.Select(ToToolProduct).ToArray(),
            null,
            null);
    }

    [McpServerTool(
        Name = "get_product",
        Title = "Get product",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false,
        Idempotent = true,
        UseStructuredContent = true)]
    [Description("Get one current product from the internal catalog by its UUID. Never changes inventory.")]
    public static async Task<GetProductToolResult> GetProduct(
        IReadOnlyProductCatalog catalog,
        [Description("Product UUID returned by search_products.")] string productId,
        CancellationToken cancellationToken = default)
    {
        if (!Guid.TryParse(productId, out var id) || id == Guid.Empty)
        {
            return new GetProductToolResult(
                false,
                null,
                "VALIDATION_ERROR",
                "productId must be a non-empty UUID.");
        }

        var product = await catalog.GetAsync(id, cancellationToken);
        return product is null
            ? new GetProductToolResult(false, null, "PRODUCT_NOT_FOUND", "Product was not found.")
            : new GetProductToolResult(true, ToToolProduct(product), null, null);
    }

    [McpServerTool(
        Name = "check_availability",
        Title = "Check product availability",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false,
        Idempotent = true,
        UseStructuredContent = true)]
    [Description("Check current stock availability for up to twenty distinct catalog products. This is a point-in-time read; closing an invoice must revalidate stock.")]
    public static async Task<CheckAvailabilityToolResult> CheckAvailability(
        IReadOnlyProductCatalog catalog,
        [Description("Distinct product UUIDs and positive quantities to check.")] IReadOnlyList<AvailabilityToolInput> items,
        CancellationToken cancellationToken = default)
    {
        if (items is null || items.Count is < 1 or > 20)
        {
            return InvalidAvailability("items must contain between 1 and 20 entries.");
        }

        var parsed = new List<AvailabilityItemRequest>(items.Count);
        foreach (var item in items)
        {
            if (!Guid.TryParse(item.ProductId, out var productId) ||
                productId == Guid.Empty ||
                item.Quantity <= 0)
            {
                return InvalidAvailability("Every item requires a non-empty product UUID and a positive integer quantity.");
            }

            parsed.Add(new AvailabilityItemRequest(productId, item.Quantity));
        }

        if (parsed.Select(item => item.ProductId).Distinct().Count() != parsed.Count)
        {
            return InvalidAvailability("A product can appear only once in an availability check.");
        }

        var availability = await catalog.CheckAvailabilityAsync(parsed, cancellationToken);
        var results = availability.Select(item => new ProductAvailabilityToolItem(
            item.ProductId.ToString(),
            item.Code,
            item.Description,
            item.RequestedQuantity,
            item.AvailableBalance,
            item.Exists,
            item.IsAvailable)).ToArray();

        return new CheckAvailabilityToolResult(
            results.All(item => item.IsAvailable),
            results,
            null,
            null);
    }

    [McpServerTool(
        Name = "list_products",
        Title = "List products",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false,
        Idempotent = true,
        UseStructuredContent = true)]
    [Description("List the internal product catalog page by page, without a search term. Use it to answer what exists in stock. Never changes inventory.")]
    public static async Task<ListProductsToolResult> ListProducts(
        IReadOnlyProductCatalog catalog,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Items per page; from 1 to 50.")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (page < 1)
        {
            return new ListProductsToolResult([], 1, 0, 0, "VALIDATION_ERROR", "page must be 1 or greater.");
        }

        if (pageSize is < 1 or > 50)
        {
            return new ListProductsToolResult([], page, 0, 0, "VALIDATION_ERROR", "pageSize must be between 1 and 50.");
        }

        // Consulta nula lista tudo: o catalogo ja trata isso e pagina.
        var result = await catalog.SearchAsync(null, page, pageSize, cancellationToken);
        return new ListProductsToolResult(
            result.Items.Select(ToToolProduct).ToArray(),
            result.Page,
            result.PageSize,
            result.TotalCount,
            null,
            null);
    }

    [McpServerTool(
        Name = "list_movements",
        Title = "List stock movements",
        ReadOnly = true,
        OpenWorld = false,
        Destructive = false,
        Idempotent = true,
        UseStructuredContent = true)]
    [Description("List completed stock movements, newest first, optionally for one product. Read-only history; never changes inventory.")]
    public static async Task<ListMovementsToolResult> ListMovements(
        IStockMovementReader reader,
        [Description("Optional product UUID to filter by.")] string? productId = null,
        [Description("Page number, starting at 1.")] int page = 1,
        [Description("Items per page; from 1 to 50.")] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        Guid? filtro = null;
        if (!string.IsNullOrWhiteSpace(productId))
        {
            if (!Guid.TryParse(productId, out var id) || id == Guid.Empty)
            {
                return new ListMovementsToolResult([], page, 0, 0, "VALIDATION_ERROR", "productId must be a non-empty UUID.");
            }

            filtro = id;
        }

        if (page < 1 || pageSize is < 1 or > 50)
        {
            return new ListMovementsToolResult([], page, 0, 0, "VALIDATION_ERROR", "page must be 1 or greater and pageSize between 1 and 50.");
        }

        var result = await reader.SearchAsync(filtro, page, pageSize, cancellationToken);
        var items = result.Items.Select(movement => new MovementToolItem(
            movement.ProductId.ToString(),
            movement.Code,
            movement.Description,
            movement.Quantity,
            movement.BalanceBefore,
            movement.BalanceAfter,
            movement.InvoiceId.ToString(),
            movement.CreatedAt)).ToArray();

        return new ListMovementsToolResult(items, result.Page, result.PageSize, result.TotalCount, null, null);
    }

    private static ProductToolItem ToToolProduct(ProductResponse product) =>
        new(product.Id.ToString(), product.Code, product.Description, product.Balance);

    private static CheckAvailabilityToolResult InvalidAvailability(string message) =>
        new(false, [], "VALIDATION_ERROR", message);
}

public sealed record ProductToolItem(
    string ProductId,
    string Code,
    string Description,
    int Balance);

public sealed record SearchProductsToolResult(
    IReadOnlyList<ProductToolItem> Products,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record GetProductToolResult(
    bool Found,
    ProductToolItem? Product,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record AvailabilityToolInput(
    [property: Description("Product UUID returned by search_products.")] string ProductId,
    [property: Description("Positive integer quantity requested.")] int Quantity);

public sealed record ProductAvailabilityToolItem(
    string ProductId,
    string? Code,
    string? Description,
    int RequestedQuantity,
    int AvailableBalance,
    bool Exists,
    bool IsAvailable);

public sealed record CheckAvailabilityToolResult(
    bool AllAvailable,
    IReadOnlyList<ProductAvailabilityToolItem> Items,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record ListProductsToolResult(
    IReadOnlyList<ProductToolItem> Products,
    int Page,
    int PageSize,
    int Total,
    string? ErrorCode,
    string? ErrorMessage);

public sealed record MovementToolItem(
    string ProductId,
    string Code,
    string Description,
    int Quantity,
    int BalanceBefore,
    int BalanceAfter,
    string InvoiceId,
    DateTimeOffset CreatedAt);

public sealed record ListMovementsToolResult(
    IReadOnlyList<MovementToolItem> Movements,
    int Page,
    int PageSize,
    int Total,
    string? ErrorCode,
    string? ErrorMessage);
