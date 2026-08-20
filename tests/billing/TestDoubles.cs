using System.Net;
using System.Text;
using Billing.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Billing.Api.Tests;

internal sealed class InMemoryBillingDbFactory(string name) : IDbContextFactory<BillingDbContext>
{
    private readonly DbContextOptions<BillingDbContext> _options = new DbContextOptionsBuilder<BillingDbContext>()
        .UseInMemoryDatabase(name)
        .Options;

    public BillingDbContext CreateDbContext() => new(_options);
    public Task<BillingDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult(CreateDbContext());
}


internal sealed class FakeInventoryClient : IInventoryClient
{
    public Dictionary<Guid, InventoryProduct> Products { get; } = [];
    public Queue<object> DebitResults { get; } = new();
    public Queue<object> QueryResults { get; } = new();
    public int DebitCalls { get; private set; }
    public int QueryCalls { get; private set; }

    public Task<InventoryProduct?> GetProductAsync(Guid id, CancellationToken cancellationToken) =>
        Task.FromResult(Products.GetValueOrDefault(id));

    public Task<InventoryProduct?> FindByCodeAsync(string code, CancellationToken cancellationToken) =>
        Task.FromResult(Products.Values.FirstOrDefault(product =>
            string.Equals(product.Code, code, StringComparison.OrdinalIgnoreCase)));

    /// <summary>Produtos criados pelo fluxo de ação proposta, para inspeção nos testes.</summary>
    public List<InventoryProduct> Created { get; } = [];

    public Task<InventoryProduct> CreateProductAsync(
        string code,
        string description,
        int balance,
        bool tracksStock,
        string actorName,
        CancellationToken cancellationToken)
    {
        var product = new InventoryProduct(Guid.NewGuid(), code, description, balance, tracksStock);
        Products[product.Id] = product;
        Created.Add(product);
        return Task.FromResult(product);
    }

    public Task<InventoryProduct> UpdateProductAsync(Guid id, string code, string description, bool tracksStock, Guid version, string actorName, CancellationToken cancellationToken)
    {
        var product = new InventoryProduct(id, code, description, Products.GetValueOrDefault(id)?.Balance ?? 0, tracksStock);
        Products[id] = product;
        return Task.FromResult(product);
    }

    public Task<StockDebitOutcome> DebitAsync(Guid attemptId, Guid invoiceId, IReadOnlyList<StockDebitItem> items, CancellationToken cancellationToken)
    {
        DebitCalls++;
        return Task.FromResult(Next(DebitResults));
    }

    public Task<StockDebitOutcome> GetDebitAsync(Guid attemptId, CancellationToken cancellationToken)
    {
        QueryCalls++;
        return Task.FromResult(Next(QueryResults));
    }

    private static StockDebitOutcome Next(Queue<object> results)
    {
        if (results.Count == 0) return new("Completed");
        var next = results.Dequeue();
        if (next is Exception exception) throw exception;
        return (StockDebitOutcome)next;
    }
}

internal sealed class QueueHttpMessageHandler(params string[] responseBodies) : HttpMessageHandler
{
    private readonly Queue<string> _bodies = new(responseBodies);
    public List<HttpRequestMessage> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);
        foreach (var header in request.Headers) clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        if (request.Content is not null)
            clone.Content = new StringContent(await request.Content.ReadAsStringAsync(cancellationToken), Encoding.UTF8, "application/json");
        Requests.Add(clone);
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(_bodies.Dequeue(), Encoding.UTF8, "application/json")
        };
    }
}

internal static class TestLoggers
{
    public static NullLogger<T> For<T>() => NullLogger<T>.Instance;
}
