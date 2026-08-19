using System.Text.Json;
using Billing.Api.Contracts;
using Billing.Api.Infrastructure;
using Billing.Api.Options;

namespace Billing.Api.Tests;

public sealed class OpenAiResponsesClientTests
{
    [Fact]
    public async Task Executes_discovered_mcp_tools_and_derives_availability_from_inventory()
    {
        var product = new FakeProduct(Guid.NewGuid(), "MOU-1", "Mouse real", 1);
        var handler = new QueueHttpMessageHandler(
            ToolResponse("r1", "search_products", "c1", "{\"query\":\"mouse\",\"limit\":5}"),
            ToolResponse("r2", "check_availability", "c2", $$"""{"items":[{"productId":"{{product.Id}}","quantity":2}]}"""),
            FinalResponse(product.Id, 2, "available"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var client = CreateClient(httpClient, new FakeToolSession([product]));

        var result = await client.GenerateAsync(new AiDraftInput("dois mouses", null, null), CancellationToken.None);

        var item = Assert.Single(result.Items);
        Assert.Equal("MOU-1", item.Code);
        Assert.Equal("Mouse real", item.Description);
        Assert.Equal("insufficient", item.Availability);
        Assert.Equal(["search_products", "check_availability"], result.ToolNames);
        Assert.Equal(3, handler.Requests.Count);
        Assert.All(handler.Requests, request => Assert.Contains("\"store\":false", request.Content!.ReadAsStringAsync().Result));
        Assert.All(handler.Requests, request => Assert.DoesNotContain("previous_response_id", request.Content!.ReadAsStringAsync().Result));
    }

    [Fact]
    public async Task Rejects_final_product_that_was_not_checked()
    {
        var checkedProduct = new FakeProduct(Guid.NewGuid(), "A-1", "Produto A", 10);
        var uncheckedProduct = new FakeProduct(Guid.NewGuid(), "B-1", "Produto B", 10);
        var handler = new QueueHttpMessageHandler(
            ToolResponse("r1", "search_products", "c1", "{\"query\":\"produto\",\"limit\":5}"),
            ToolResponse("r2", "check_availability", "c2", $$"""{"items":[{"productId":"{{checkedProduct.Id}}","quantity":1}]}"""),
            FinalResponse(uncheckedProduct.Id, 1, "available"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var client = CreateClient(httpClient, new FakeToolSession([checkedProduct, uncheckedProduct]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GenerateAsync(new AiDraftInput("produto B", null, null), CancellationToken.None));

        Assert.Contains("availability proof", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Rejects_final_quantity_that_differs_from_checked_quantity()
    {
        var product = new FakeProduct(Guid.NewGuid(), "A-1", "Produto A", 10);
        var handler = new QueueHttpMessageHandler(
            ToolResponse("r1", "search_products", "c1", "{\"query\":\"produto\",\"limit\":5}"),
            ToolResponse("r2", "check_availability", "c2", $$"""{"items":[{"productId":"{{product.Id}}","quantity":1}]}"""),
            FinalResponse(product.Id, 2, "available"));
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var client = CreateClient(httpClient, new FakeToolSession([product]));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GenerateAsync(new AiDraftInput("dois produtos", null, null), CancellationToken.None));

        Assert.Contains("availability proof", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Rejects_protocol_and_semantic_mcp_tool_failures(bool protocolError)
    {
        var product = new FakeProduct(Guid.NewGuid(), "A-1", "Produto A", 10);
        var handler = new QueueHttpMessageHandler(
            ToolResponse("r1", "search_products", "c1", "{\"query\":\"produto\",\"limit\":5}"),
            ToolResponse("r2", "check_availability", "c2", $$"""{"items":[{"productId":"{{product.Id}}","quantity":1}]}"""));
        var session = new FakeToolSession(
            [product],
            (name, _) => name == "check_availability"
                ? protocolError
                    ? new AiToolResult(JsonSerializer.SerializeToElement(new { }), true)
                    : new AiToolResult(JsonSerializer.SerializeToElement(new
                    {
                        allAvailable = false,
                        items = Array.Empty<object>(),
                        errorCode = "CATALOG_FAILURE",
                        errorMessage = "catalog failed"
                    }), false)
                : null);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://api.openai.com/v1/") };
        var client = CreateClient(httpClient, session);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            client.GenerateAsync(new AiDraftInput("um produto", null, null), CancellationToken.None));

        Assert.Contains("error", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static OpenAiResponsesClient CreateClient(HttpClient httpClient, FakeToolSession session) =>
        new(
            httpClient,
            new FakeToolSessionFactory(session),
            Microsoft.Extensions.Options.Options.Create(new OpenAiOptions { ApiKey = "test", MaxToolCalls = 8 }),
            TestLoggers.For<OpenAiResponsesClient>());

    private static string ToolResponse(string id, string name, string callId, string arguments) => $$"""
        {"id":"{{id}}","usage":{"input_tokens":1,"output_tokens":1},"output":[{"type":"function_call","name":"{{name}}","call_id":"{{callId}}","arguments":{{JsonSerializer.Serialize(arguments)}}}]}
        """;

    private static string FinalResponse(Guid productId, int quantity, string availability)
    {
        var draft = JsonSerializer.Serialize(new
        {
            items = new[]
            {
                new
                {
                    productId,
                    code = "MODEL-CODE",
                    description = "Model description",
                    quantity,
                    availability
                }
            },
            unresolvedItems = Array.Empty<object>(),
            warnings = Array.Empty<string>()
        });
        return JsonSerializer.Serialize(new
        {
            id = "final",
            usage = new { input_tokens = 4, output_tokens = 5 },
            output = new[]
            {
                new
                {
                    type = "message",
                    content = new[] { new { type = "output_text", text = draft } }
                }
            }
        });
    }

    private sealed class FakeToolSessionFactory(FakeToolSession session) : IInventoryToolSessionFactory
    {
        public Task<IInventoryToolSession> OpenAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IInventoryToolSession>(session);
    }

    private sealed class FakeToolSession(
        IReadOnlyList<FakeProduct> products,
        Func<string, JsonElement, AiToolResult?>? overrideCall = null) : IInventoryToolSession
    {
        private static readonly JsonElement EmptySchema = JsonDocument.Parse("{\"type\":\"object\"}").RootElement.Clone();

        public IReadOnlyList<AiToolDefinition> Tools { get; } =
        [
            new("search_products", "search", EmptySchema),
            new("get_product", "get", EmptySchema),
            new("check_availability", "availability", EmptySchema)
        ];

        public Task<AiToolResult> CallAsync(string name, JsonElement arguments, CancellationToken cancellationToken)
        {
            var overridden = overrideCall?.Invoke(name, arguments);
            if (overridden is not null) return Task.FromResult(overridden);

            if (name == "search_products")
            {
                return Task.FromResult(new AiToolResult(JsonSerializer.SerializeToElement(new
                {
                    products = products.Select(ToWireProduct).ToArray(),
                    errorCode = (string?)null,
                    errorMessage = (string?)null
                }), false));
            }

            if (name == "check_availability")
            {
                var items = arguments.GetProperty("items").EnumerateArray().Select(item =>
                {
                    var productId = Guid.Parse(item.GetProperty("productId").GetString()!);
                    var quantity = item.GetProperty("quantity").GetInt32();
                    var product = products.SingleOrDefault(candidate => candidate.Id == productId);
                    return new AvailabilityWireItem(
                        productId,
                        product?.Code,
                        product?.Description,
                        quantity,
                        product?.Balance ?? 0,
                        product is not null,
                        product is not null && product.Balance >= quantity);
                }).ToArray();
                return Task.FromResult(new AiToolResult(JsonSerializer.SerializeToElement(new
                {
                    allAvailable = items.All(item => item.IsAvailable),
                    items,
                    errorCode = (string?)null,
                    errorMessage = (string?)null
                }), false));
            }

            var requestedId = Guid.Parse(arguments.GetProperty("productId").GetString()!);
            var requestedProduct = products.SingleOrDefault(product => product.Id == requestedId);
            return Task.FromResult(new AiToolResult(JsonSerializer.SerializeToElement(new
            {
                found = requestedProduct is not null,
                product = requestedProduct is null ? null : ToWireProduct(requestedProduct),
                errorCode = requestedProduct is null ? "PRODUCT_NOT_FOUND" : null,
                errorMessage = requestedProduct is null ? "not found" : null
            }), false));
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        private static object ToWireProduct(FakeProduct product) => new
        {
            productId = product.Id,
            product.Code,
            product.Description,
            product.Balance
        };
    }

    private sealed record FakeProduct(Guid Id, string Code, string Description, int Balance);
    private sealed record AvailabilityWireItem(
        Guid ProductId,
        string? Code,
        string? Description,
        int RequestedQuantity,
        int AvailableBalance,
        bool Exists,
        bool IsAvailable);
}
