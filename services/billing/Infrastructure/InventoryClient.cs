using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Billing.Api.Domain;

namespace Billing.Api.Infrastructure;

public interface IInventoryClient
{
    Task<InventoryProduct?> GetProductAsync(Guid id, CancellationToken cancellationToken);
    Task<InventoryProduct?> FindByCodeAsync(string code, CancellationToken cancellationToken);
    Task<InventoryProduct> CreateProductAsync(
        string code,
        string description,
        int balance,
        bool tracksStock,
        string actorName,
        CancellationToken cancellationToken);
    Task<InventoryProduct> UpdateProductAsync(Guid id, string code, string description, bool tracksStock, Guid version, string actorName, CancellationToken cancellationToken);
    Task<StockDebitOutcome> DebitAsync(Guid attemptId, Guid invoiceId, IReadOnlyList<StockDebitItem> items, CancellationToken cancellationToken);
    Task<StockDebitOutcome> GetDebitAsync(Guid attemptId, CancellationToken cancellationToken);
}

/// <summary>
/// Projeção do produto vista pelo Billing. `TracksStock` importa aqui: item sem
/// controle não deve ser barrado por saldo em nenhuma validação antecipada.
/// </summary>
public sealed record InventoryProduct(
    Guid Id,
    string Code,
    string Description,
    int Balance,
    bool TracksStock = true,
    DateTimeOffset? CreatedAt = null,
    string? CreatedBy = null,
    DateTimeOffset? UpdatedAt = null,
    string? UpdatedBy = null,
    Guid? Version = null);
public sealed record StockDebitItem(Guid ProductId, int Quantity);
/// <summary>Item que entrou na nota sem movimentar estoque (INV-04).</summary>
public sealed record IgnoredStockItem(Guid ProductId, string Code, int Quantity, string Reason, string Message);

public sealed record StockDebitOutcome(
    string State,
    string? ErrorCode = null,
    string? ErrorMessage = null,
    bool NotFound = false,
    IReadOnlyList<IgnoredStockItem>? IgnoredItems = null)
{
    public bool IsCompleted => State.Equals("Completed", StringComparison.OrdinalIgnoreCase);
    public bool IsRejected => State.Equals("Rejected", StringComparison.OrdinalIgnoreCase);
}

public sealed class InventoryClient(HttpClient httpClient, ILogger<InventoryClient> logger) : IInventoryClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<InventoryProduct?> GetProductAsync(Guid id, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => httpClient.GetAsync($"/api/products/{id}", cancellationToken));
        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw Unavailable();
        return await response.Content.ReadFromJsonAsync<InventoryProduct>(JsonOptions, cancellationToken)
            ?? throw Unavailable();
    }

    /// <summary>
    /// Resolve um código do catálogo. A busca do Inventory é por texto, então
    /// filtramos por igualdade exata aqui — "CAB-1" não pode casar "CAB-10".
    /// </summary>
    public async Task<InventoryProduct?> FindByCodeAsync(string code, CancellationToken cancellationToken)
    {
        var query = Uri.EscapeDataString(code.Trim());
        using var response = await SendAsync(() =>
            httpClient.GetAsync($"/api/products?query={query}&page=1&pageSize=20", cancellationToken));

        if (response.StatusCode == HttpStatusCode.NotFound) return null;
        if (!response.IsSuccessStatusCode) throw Unavailable();

        var page = await response.Content.ReadFromJsonAsync<ProductPageWireResponse>(JsonOptions, cancellationToken);
        return page?.Items?.FirstOrDefault(product =>
            string.Equals(product.Code, code.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Cadastra um produto pela API do Inventory. O Billing não escreve no banco
    /// do outro serviço (INV-05); a porta é esta, e a dona do saldo continua sendo
    /// o Inventory (INV-02).
    /// </summary>
    public async Task<InventoryProduct> CreateProductAsync(
        string code,
        string description,
        int balance,
        bool tracksStock,
        string actorName,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/products")
        { Content = JsonContent.Create(new { code, description, balance, tracksStock }, options: JsonOptions) };
        request.Headers.Add("X-Notaflow-Actor", actorName);
        using var response = await SendAsync(() => httpClient.SendAsync(request, cancellationToken));

        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new ConflictException("PRODUCT_CODE_TAKEN", $"Já existe um produto com o código {code}.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new DomainValidationException("O Inventory recusou os dados do produto.");
        if (!response.IsSuccessStatusCode) throw Unavailable();

        return await response.Content.ReadFromJsonAsync<InventoryProduct>(JsonOptions, cancellationToken)
            ?? throw Unavailable();
    }

    public async Task<InventoryProduct> UpdateProductAsync(Guid id, string code, string description, bool tracksStock, Guid version, string actorName, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/internal/products/{id}")
        { Content = JsonContent.Create(new { code, description, tracksStock }, options: JsonOptions) };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{version}\"");
        request.Headers.Add("X-Notaflow-Actor", actorName);
        using var response = await SendAsync(() => httpClient.SendAsync(request, cancellationToken));
        if (response.StatusCode == HttpStatusCode.Conflict)
            throw new ConflictException("PRODUCT_UPDATE_CONFLICT", "O produto foi alterado ou não pode ser atualizado neste estado.");
        if (response.StatusCode == HttpStatusCode.BadRequest)
            throw new DomainValidationException("O Inventory recusou os dados do produto.");
        if (!response.IsSuccessStatusCode) throw Unavailable();
        return await response.Content.ReadFromJsonAsync<InventoryProduct>(JsonOptions, cancellationToken) ?? throw Unavailable();
    }

    public async Task<StockDebitOutcome> DebitAsync(
        Guid attemptId,
        Guid invoiceId,
        IReadOnlyList<StockDebitItem> items,
        CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/stock/debits")
        {
            Content = JsonContent.Create(new { invoiceId, items })
        };
        request.Headers.Add("Idempotency-Key", attemptId.ToString());

        using var response = await SendAsync(() => httpClient.SendAsync(request, cancellationToken));
        return await ParseDebitResponse(response, cancellationToken);
    }

    public async Task<StockDebitOutcome> GetDebitAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        using var response = await SendAsync(() => httpClient.GetAsync($"/api/stock/debits/{attemptId}", cancellationToken));
        if (response.StatusCode == HttpStatusCode.NotFound)
            return new("Pending", NotFound: true);
        return await ParseDebitResponse(response, cancellationToken);
    }

    private static async Task<StockDebitOutcome> ParseDebitResponse(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        StockDebitWireResponse? payload = null;
        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!string.IsNullOrWhiteSpace(body))
                payload = JsonSerializer.Deserialize<StockDebitWireResponse>(body, JsonOptions);
        }
        catch (Exception exception) when (exception is JsonException or NotSupportedException or HttpRequestException)
        {
            if (response.IsSuccessStatusCode) throw Unavailable();
        }

        if (response.IsSuccessStatusCode)
        {
            if (payload?.OperationId == Guid.Empty ||
                payload?.State is not ("Completed" or "Rejected"))
                throw Unavailable();

            return new(payload.State, payload.ErrorCode, payload.ErrorMessage, false, payload.IgnoredItems);
        }

        if (IsPermanentClientError(response.StatusCode))
        {
            if (payload?.OperationId != Guid.Empty && payload?.State == "Rejected")
                return new("Rejected", payload.ErrorCode ?? "INVENTORY_REJECTED", payload.ErrorMessage ?? "O estoque rejeitou a baixa.");

            if (!string.IsNullOrWhiteSpace(payload?.Code))
                return new("Rejected", payload.Code, payload.Detail ?? "O estoque rejeitou a baixa.");

            return new("Rejected", FallbackCode(response.StatusCode), "O estoque rejeitou permanentemente a solicitação de baixa.");
        }

        throw Unavailable();
    }

    private static bool IsPermanentClientError(HttpStatusCode statusCode) => statusCode is
        HttpStatusCode.BadRequest or
        HttpStatusCode.Unauthorized or
        HttpStatusCode.Forbidden or
        HttpStatusCode.NotFound or
        HttpStatusCode.Conflict or
        HttpStatusCode.UnprocessableEntity;

    private static string FallbackCode(HttpStatusCode statusCode) => statusCode switch
    {
        HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden => "INVENTORY_AUTH_FAILED",
        HttpStatusCode.NotFound => "INVENTORY_ENDPOINT_NOT_FOUND",
        _ => "INVENTORY_REQUEST_REJECTED"
    };

    private async Task<HttpResponseMessage> SendAsync(Func<Task<HttpResponseMessage>> action)
    {
        try
        {
            return await action();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            logger.LogWarning("Inventory dependency call failed: {ExceptionType}", ex.GetType().Name);
            throw Unavailable();
        }
    }

    private static DependencyUnavailableException Unavailable() => new(
        "INVENTORY_UNAVAILABLE",
        "O serviço de estoque está temporariamente indisponível.");

    private sealed record ProductPageWireResponse(IReadOnlyList<InventoryProduct>? Items);

    private sealed record StockDebitWireResponse(
        Guid OperationId,
        string? State,
        string? ErrorCode,
        string? ErrorMessage,
        string? Code,
        string? Detail,
        IReadOnlyList<IgnoredStockItem>? IgnoredItems);
}
