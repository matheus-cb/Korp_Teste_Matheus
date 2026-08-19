using Inventory.Api.Application;
using Inventory.Api.Contracts;
using Inventory.Api.Mcp;
using ModelContextProtocol.Server;

namespace Inventory.UnitTests;

public sealed class InventoryMcpToolsTests
{
    [Fact]
    public void ToolsExposeExactReadOnlyClosedWorldContracts()
    {
        var tools = typeof(InventoryMcpTools)
            .GetMethods()
            .Select(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), false)
                .Cast<McpServerToolAttribute>()
                .SingleOrDefault())
            .Where(attribute => attribute is not null)
            .Cast<McpServerToolAttribute>()
            .ToDictionary(attribute => attribute.Name!, StringComparer.Ordinal);

        Assert.Equal(
            ["check_availability", "get_product", "search_products"],
            tools.Keys.Order(StringComparer.Ordinal));
        Assert.All(tools.Values, attribute =>
        {
            Assert.True(attribute.ReadOnly);
            Assert.False(attribute.OpenWorld);
            Assert.False(attribute.Destructive);
            Assert.True(attribute.Idempotent);
            Assert.True(attribute.UseStructuredContent);
        });
    }

    [Fact]
    public async Task SearchProductsRejectsOversizedQueryWithoutTouchingCatalog()
    {
        var catalog = new FakeProductCatalog();

        var result = await InventoryMcpTools.SearchProducts(
            catalog,
            new string('x', 101),
            5);

        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Empty(result.Products);
        Assert.Equal(0, catalog.SearchCalls);
    }

    [Fact]
    public async Task CheckAvailabilityRejectsDuplicateProductsWithoutTouchingCatalog()
    {
        var catalog = new FakeProductCatalog();
        var productId = Guid.NewGuid().ToString();

        var result = await InventoryMcpTools.CheckAvailability(
            catalog,
            [new(productId, 1), new(productId, 2)]);

        Assert.False(result.AllAvailable);
        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Equal(0, catalog.AvailabilityCalls);
    }

    [Fact]
    public async Task SearchProductsReturnsStructuredCatalogData()
    {
        var catalog = new FakeProductCatalog
        {
            SearchResult = new ProductPageResponse(
            [
                new ProductResponse(
                    Guid.Parse("1837a925-9df2-4783-b3d6-fc520bf20034"),
                    "KEY-01",
                    "Keyboard",
                    7,
                    true,
                    new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero))
            ],
            1,
            5,
            1)
        };

        var result = await InventoryMcpTools.SearchProducts(catalog, "keyboard", 5);

        Assert.Null(result.ErrorCode);
        var product = Assert.Single(result.Products);
        Assert.Equal("KEY-01", product.Code);
        Assert.Equal(7, product.Balance);
    }

    private sealed class FakeProductCatalog : IProductCatalog
    {
        public int SearchCalls { get; private set; }
        public int AvailabilityCalls { get; private set; }
        public ProductPageResponse SearchResult { get; init; } = new([], 1, 5, 0);

        public Task<ProductResponse> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<ProductResponse?> GetAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult<ProductResponse?>(null);

        public Task<ProductPageResponse> SearchAsync(
            string? query,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            SearchCalls++;
            return Task.FromResult(SearchResult);
        }

        public Task<IReadOnlyList<ProductAvailabilityResponse>> CheckAvailabilityAsync(
            IReadOnlyList<AvailabilityItemRequest> items,
            CancellationToken cancellationToken)
        {
            AvailabilityCalls++;
            return Task.FromResult<IReadOnlyList<ProductAvailabilityResponse>>([]);
        }
    }
}
