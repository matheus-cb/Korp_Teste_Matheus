using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.IntegrationTests;

public sealed class FakeInventoryServer : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly object gate = new();
    private readonly Dictionary<Guid, FakeProduct> products = [];
    private readonly Dictionary<Guid, FakeDebitOperation> operations = [];
    private readonly List<Guid> postAttemptIds = [];
    private readonly List<Guid> getAttemptIds = [];
    private WebApplication? application;
    private TaskCompletionSource concurrentDebitBarrier = NewBarrier();
    private int expectedConcurrentDebitRequests;
    private int concurrentDebitArrivals;
    private int hideNextCompletedResult;
    private int debitPostCount;
    private int debitGetCount;
    private int debitSideEffectCount;

    public Uri BaseAddress { get; private set; } = null!;
    public int DebitPostCount => Volatile.Read(ref debitPostCount);
    public int DebitGetCount => Volatile.Read(ref debitGetCount);
    public int DebitSideEffectCount => Volatile.Read(ref debitSideEffectCount);

    public IReadOnlyList<Guid> PostAttemptIds
    {
        get
        {
            lock (gate) return postAttemptIds.ToArray();
        }
    }

    public IReadOnlyList<Guid> GetAttemptIds
    {
        get
        {
            lock (gate) return getAttemptIds.ToArray();
        }
    }

    public async Task StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(FakeInventoryServer).Assembly.FullName,
            EnvironmentName = "Testing"
        });
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));

        application = builder.Build();
        application.MapGet("/api/products/{id:guid}", GetProduct);
        application.MapPost("/api/stock/debits", DebitAsync);
        application.MapGet("/api/stock/debits/{attemptId:guid}", GetDebit);
        await application.StartAsync();

        var addresses = application.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()
            ?.Addresses;
        BaseAddress = new Uri(addresses?.Single()
            ?? throw new InvalidOperationException("The fake Inventory server did not publish an address."));
    }

    public void Reset()
    {
        lock (gate)
        {
            products.Clear();
            operations.Clear();
            postAttemptIds.Clear();
            getAttemptIds.Clear();
            concurrentDebitBarrier = NewBarrier();
            expectedConcurrentDebitRequests = 0;
            concurrentDebitArrivals = 0;
        }

        Volatile.Write(ref hideNextCompletedResult, 0);
        Volatile.Write(ref debitPostCount, 0);
        Volatile.Write(ref debitGetCount, 0);
        Volatile.Write(ref debitSideEffectCount, 0);
    }

    public void AddProduct(Guid id, string code, string description, int balance)
    {
        lock (gate)
            products.Add(id, new FakeProduct(id, code, description, balance));
    }

    public int GetBalance(Guid productId)
    {
        lock (gate)
            return products[productId].Balance;
    }

    public void WaitForConcurrentDebitRequests(int count)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(count, 2);
        lock (gate)
        {
            expectedConcurrentDebitRequests = count;
            concurrentDebitArrivals = 0;
            concurrentDebitBarrier = NewBarrier();
        }
    }

    public void HideNextCompletedDebitResult() => Volatile.Write(ref hideNextCompletedResult, 1);

    public async ValueTask DisposeAsync()
    {
        if (application is not null)
            await application.DisposeAsync();
    }

    private IResult GetProduct(Guid id)
    {
        lock (gate)
        {
            if (!products.TryGetValue(id, out var product)) return Results.NotFound();
            return Results.Ok(new
            {
                product.Id,
                product.Code,
                product.Description,
                product.Balance,
                createdAt = DateTimeOffset.UtcNow
            });
        }
    }

    private async Task DebitAsync(HttpContext context)
    {
        Interlocked.Increment(ref debitPostCount);
        if (!TryReadAttemptId(context, out var attemptId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var request = await JsonSerializer.DeserializeAsync<FakeDebitRequest>(
            context.Request.Body,
            JsonOptions,
            context.RequestAborted);
        if (request is null)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        FakeDebitOperation operation;
        Task? barrierTask = null;
        lock (gate)
        {
            postAttemptIds.Add(attemptId);
            var signature = ComputeSignature(request);
            if (operations.TryGetValue(attemptId, out var existing))
            {
                if (!string.Equals(existing.RequestSignature, signature, StringComparison.Ordinal))
                {
                    context.Response.StatusCode = StatusCodes.Status409Conflict;
                    operation = existing;
                }
                else
                {
                    operation = existing;
                }
            }
            else
            {
                foreach (var item in request.Items)
                {
                    if (!products.TryGetValue(item.ProductId, out var product) || product.Balance < item.Quantity)
                        throw new InvalidOperationException("The integration-test Inventory was seeded with insufficient stock.");
                }

                foreach (var item in request.Items)
                    products[item.ProductId].Balance -= item.Quantity;

                operation = new FakeDebitOperation(Guid.NewGuid(), signature);
                operations.Add(attemptId, operation);
                Interlocked.Increment(ref debitSideEffectCount);
            }

            if (expectedConcurrentDebitRequests > 0)
            {
                concurrentDebitArrivals++;
                if (concurrentDebitArrivals >= expectedConcurrentDebitRequests)
                    concurrentDebitBarrier.TrySetResult();
                barrierTask = concurrentDebitBarrier.Task;
            }
        }

        if (barrierTask is not null)
            await barrierTask.WaitAsync(TimeSpan.FromSeconds(10), context.RequestAborted);

        if (Interlocked.CompareExchange(ref hideNextCompletedResult, 0, 1) == 1)
        {
            // The fake Inventory committed the operation, but Billing receives no
            // authoritative result (equivalent to a lost/replaced upstream response).
            context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
            await context.Response.WriteAsJsonAsync(
                new { title = "Upstream response unavailable" },
                context.RequestAborted);
            return;
        }

        await context.Response.WriteAsJsonAsync(new
        {
            operationId = operation.Id,
            state = "Completed",
            errorCode = (string?)null,
            errorMessage = (string?)null
        }, context.RequestAborted);
    }

    private IResult GetDebit(Guid attemptId)
    {
        Interlocked.Increment(ref debitGetCount);
        lock (gate)
        {
            getAttemptIds.Add(attemptId);
            if (!operations.TryGetValue(attemptId, out var operation)) return Results.NotFound();
            return Results.Ok(new
            {
                operationId = operation.Id,
                state = "Completed",
                errorCode = (string?)null,
                errorMessage = (string?)null
            });
        }
    }

    private static bool TryReadAttemptId(HttpContext context, out Guid attemptId) =>
        Guid.TryParse(context.Request.Headers["Idempotency-Key"].SingleOrDefault(), out attemptId) &&
        attemptId != Guid.Empty;

    private static string ComputeSignature(FakeDebitRequest request) =>
        string.Join(
            '|',
            new[] { request.InvoiceId.ToString("N") }
                .Concat(request.Items
                    .OrderBy(item => item.ProductId)
                    .Select(item => $"{item.ProductId:N}:{item.Quantity}")));

    private static TaskCompletionSource NewBarrier() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    private sealed class FakeProduct(Guid id, string code, string description, int balance)
    {
        public Guid Id { get; } = id;
        public string Code { get; } = code;
        public string Description { get; } = description;
        public int Balance { get; set; } = balance;
    }

    private sealed record FakeDebitOperation(Guid Id, string RequestSignature);
    private sealed record FakeDebitRequest(Guid InvoiceId, IReadOnlyList<FakeDebitItem> Items);
    private sealed record FakeDebitItem(Guid ProductId, int Quantity);
}
