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

        // Lista fechada de proposito: uma tool nova so entra aqui depois de
        // alguem conferir que ela e mesmo somente leitura (INV-27).
        Assert.Equal(
            ["check_availability", "get_product", "list_movements", "list_products", "search_products"],
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

    /// <summary>
    /// INV-27 estrutural: a anotacao <c>ReadOnly</c> descreve a intencao, mas nao
    /// impede nada — quem impede e o tipo injetado. Uma tool que peca
    /// <see cref="IProductCatalog"/> volta a alcancar <c>CreateAsync</c>, e este
    /// teste e o que reprova isso antes de virar tool de escrita por acidente.
    /// </summary>
    [Fact]
    public void ToolsNeverReceiveAWriteCapableCatalog()
    {
        var offenders = typeof(InventoryMcpTools)
            .GetMethods()
            .Where(method => method.GetCustomAttributes(typeof(McpServerToolAttribute), false).Length > 0)
            .SelectMany(
                method => method.GetParameters(),
                (method, parameter) => new { method, parameter })
            .Where(x => typeof(IProductCatalog).IsAssignableFrom(x.parameter.ParameterType))
            .Select(x => $"{x.method.Name}({x.parameter.ParameterType.Name} {x.parameter.Name})")
            .ToArray();

        Assert.Empty(offenders);
    }

    [Fact]
    public async Task ListProductsRejectsOversizedPageWithoutTouchingCatalog()
    {
        var catalog = new FakeProductCatalog();

        var result = await InventoryMcpTools.ListProducts(catalog, 1, 500);

        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Empty(result.Products);
        Assert.Equal(0, catalog.SearchCalls);
    }

    /// <summary>
    /// A listagem existe para responder "o que tenho no estoque", que a busca
    /// nao respondia: ela exige termo de 1 a 100 caracteres. Aqui a consulta vai
    /// nula de proposito, e o catalogo pagina.
    /// </summary>
    [Fact]
    public async Task ListProductsSearchesWithoutQuery()
    {
        var catalog = new FakeProductCatalog();

        var result = await InventoryMcpTools.ListProducts(catalog, 1, 20);

        Assert.Null(result.ErrorCode);
        Assert.Equal(1, catalog.SearchCalls);
    }

    [Fact]
    public async Task ListMovementsRejectsInvalidProductId()
    {
        var reader = new FakeStockMovementReader();

        var result = await InventoryMcpTools.ListMovements(reader, "nao-e-uuid");

        Assert.Equal("VALIDATION_ERROR", result.ErrorCode);
        Assert.Empty(result.Movements);
        Assert.Equal(0, reader.Calls);
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
                    new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero),
                    "Sistema",
                    new DateTimeOffset(2026, 8, 13, 12, 0, 0, TimeSpan.Zero),
                    "Sistema",
                    Guid.NewGuid())
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

    // Implementa so a leitura: nao existe mais CreateAsync para o fake precisar
    // stubar, o que e a prova pratica de que a tool nao alcanca a escrita.
    private sealed class FakeStockMovementReader : IStockMovementReader
    {
        public int Calls { get; private set; }

        public Task<StockMovementPageResponse> SearchAsync(
            Guid? productId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(new StockMovementPageResponse([], page, pageSize, 0));
        }
    }

    private sealed class FakeProductCatalog : IReadOnlyProductCatalog
    {
        public int SearchCalls { get; private set; }
        public int AvailabilityCalls { get; private set; }
        public ProductPageResponse SearchResult { get; init; } = new([], 1, 5, 0);

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
