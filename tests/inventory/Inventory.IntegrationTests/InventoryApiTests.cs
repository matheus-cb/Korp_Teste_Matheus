using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Inventory.Api.Contracts;

namespace Inventory.IntegrationTests;

[Collection(InventoryApiTestGroup.Name)]
public sealed class InventoryApiTests(InventoryApiFixture fixture)
{
    [Fact]
    public async Task ProductCanBeCreatedReadAndProtectedByUniqueCode()
    {
        var code = NewCode("PRD");
        var created = await CreateProductAsync(code, 9);

        using var getResponse = await fixture.Client.GetAsync($"/api/products/{created.Id}");
        getResponse.EnsureSuccessStatusCode();
        var found = await getResponse.Content.ReadFromJsonAsync<ProductResponse>();

        Assert.NotNull(found);
        Assert.Equal(code, found.Code);
        Assert.Equal(9, found.Balance);

        using var duplicateResponse = await fixture.Client.PostAsJsonAsync(
            "/api/products",
            new CreateProductRequest(code.ToLowerInvariant(), "Duplicate", 1, true));
        Assert.Equal(HttpStatusCode.Conflict, duplicateResponse.StatusCode);
        Assert.Equal("PRODUCT_CODE_ALREADY_EXISTS", await ReadProblemCodeAsync(duplicateResponse));
    }

    [Fact]
    public async Task ProductWithoutStockControlIsNeverRejectedAndKeepsBalanceUntouched()
    {
        var service = await CreateProductAsync(NewCode("SVC"), 0, tracksStock: false);
        Assert.False(service.TracksStock);

        var request = new StockDebitRequest(
            Guid.NewGuid(),
            [new StockDebitItemRequest(service.Id, 9_999)]);

        using var response = await SendDebitAsync(Guid.NewGuid(), request);

        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<StockDebitOperationResponse>();
        Assert.NotNull(result);
        Assert.Equal("Completed", result.State);

        // Nao controla estoque: o saldo nao muda por mais alta que seja a quantidade.
        var after = await GetProductAsync(service.Id);
        Assert.Equal(0, after.Balance);
        Assert.False(after.TracksStock);
    }

    [Fact]
    public async Task MixedInvoiceDebitsOnlyTheControlledProduct()
    {
        var controlled = await CreateProductAsync(NewCode("CTL"), 10);
        var service = await CreateProductAsync(NewCode("MIX"), 0, tracksStock: false);

        var request = new StockDebitRequest(
            Guid.NewGuid(),
            [
                new StockDebitItemRequest(controlled.Id, 3),
                new StockDebitItemRequest(service.Id, 2)
            ]);

        using var response = await SendDebitAsync(Guid.NewGuid(), request);

        response.EnsureSuccessStatusCode();
        Assert.Equal(7, (await GetProductAsync(controlled.Id)).Balance);
        Assert.Equal(0, (await GetProductAsync(service.Id)).Balance);
    }

    [Fact]
    public async Task DebitIsAllOrNothingWhenOneProductHasInsufficientStock()
    {
        var available = await CreateProductAsync(NewCode("AVL"), 5);
        var unavailable = await CreateProductAsync(NewCode("EMP"), 0);
        var request = new StockDebitRequest(
            Guid.NewGuid(),
            [
                new StockDebitItemRequest(available.Id, 2),
                new StockDebitItemRequest(unavailable.Id, 1)
            ]);

        using var response = await SendDebitAsync(Guid.NewGuid(), request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<StockDebitOperationResponse>();
        Assert.NotNull(result);
        Assert.Equal("Rejected", result.State);
        Assert.Equal("INSUFFICIENT_STOCK", result.ErrorCode);
        Assert.Equal(5, (await GetProductAsync(available.Id)).Balance);
        Assert.Equal(0, (await GetProductAsync(unavailable.Id)).Balance);
    }

    [Fact]
    public async Task RepeatedRequestReturnsOriginalResultAndDebitsOnlyOnce()
    {
        var product = await CreateProductAsync(NewCode("IDM"), 10);
        var attemptId = Guid.NewGuid();
        var request = new StockDebitRequest(
            Guid.NewGuid(),
            [new StockDebitItemRequest(product.Id, 4)]);

        using var firstResponse = await SendDebitAsync(attemptId, request);
        using var repeatedResponse = await SendDebitAsync(attemptId, request);

        firstResponse.EnsureSuccessStatusCode();
        repeatedResponse.EnsureSuccessStatusCode();
        var first = await firstResponse.Content.ReadFromJsonAsync<StockDebitOperationResponse>();
        var repeated = await repeatedResponse.Content.ReadFromJsonAsync<StockDebitOperationResponse>();
        Assert.NotNull(first);
        Assert.NotNull(repeated);
        Assert.Equal(first.OperationId, repeated.OperationId);
        Assert.Equal("Completed", repeated.State);
        Assert.Equal(6, (await GetProductAsync(product.Id)).Balance);

        using var getOperation = await fixture.Client.GetAsync($"/api/stock/debits/{attemptId}");
        getOperation.EnsureSuccessStatusCode();
        var persisted = await getOperation.Content.ReadFromJsonAsync<StockDebitOperationResponse>();
        Assert.Equal(first.OperationId, persisted?.OperationId);
    }

    [Fact]
    public async Task ReusingIdempotencyKeyWithDifferentPayloadReturnsStableConflict()
    {
        var product = await CreateProductAsync(NewCode("KEY"), 10);
        var attemptId = Guid.NewGuid();
        var invoiceId = Guid.NewGuid();
        using var first = await SendDebitAsync(
            attemptId,
            new StockDebitRequest(invoiceId, [new StockDebitItemRequest(product.Id, 1)]));
        first.EnsureSuccessStatusCode();

        using var conflict = await SendDebitAsync(
            attemptId,
            new StockDebitRequest(invoiceId, [new StockDebitItemRequest(product.Id, 2)]));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REUSED", await ReadProblemCodeAsync(conflict));
        Assert.Equal(9, (await GetProductAsync(product.Id)).Balance);
    }

    [Fact]
    public async Task ConcurrentDebitsCannotConsumeTheSameLastUnit()
    {
        var product = await CreateProductAsync(NewCode("ONE"), 1);
        var firstRequest = new StockDebitRequest(
            Guid.NewGuid(),
            [new StockDebitItemRequest(product.Id, 1)]);
        var secondRequest = firstRequest with { InvoiceId = Guid.NewGuid() };

        var responses = await Task.WhenAll(
            SendDebitAsync(Guid.NewGuid(), firstRequest),
            SendDebitAsync(Guid.NewGuid(), secondRequest));

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict);
            Assert.Equal(0, (await GetProductAsync(product.Id)).Balance);
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task MissingIdempotencyKeyReturnsStableValidationProblem()
    {
        var product = await CreateProductAsync(NewCode("HDR"), 1);

        using var response = await fixture.Client.PostAsJsonAsync(
            "/api/stock/debits",
            new StockDebitRequest(Guid.NewGuid(), [new StockDebitItemRequest(product.Id, 1)]));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("IDEMPOTENCY_KEY_REQUIRED", await ReadProblemCodeAsync(response));
        Assert.Equal(1, (await GetProductAsync(product.Id)).Balance);
    }

    private async Task<ProductResponse> CreateProductAsync(
        string code,
        int balance,
        bool tracksStock = true)
    {
        using var response = await fixture.Client.PostAsJsonAsync(
            "/api/products",
            new CreateProductRequest(code, $"Product {code}", balance, tracksStock));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private async Task<ProductResponse> GetProductAsync(Guid id)
    {
        using var response = await fixture.Client.GetAsync($"/api/products/{id}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponse>())!;
    }

    private async Task<HttpResponseMessage> SendDebitAsync(Guid attemptId, StockDebitRequest request)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, "/api/stock/debits")
        {
            Content = JsonContent.Create(request)
        };
        message.Headers.TryAddWithoutValidation("Idempotency-Key", attemptId.ToString());
        return await fixture.Client.SendAsync(message);
    }

    private static async Task<string?> ReadProblemCodeAsync(HttpResponseMessage response)
    {
        await using var stream = await response.Content.ReadAsStreamAsync();
        using var json = await JsonDocument.ParseAsync(stream);
        return json.RootElement.TryGetProperty("code", out var code) ? code.GetString() : null;
    }

    private static string NewCode(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}".ToUpperInvariant();
}
