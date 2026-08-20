using System.Net;
using System.Net.Http.Json;
using Billing.Api.Application;
using Billing.Api.Contracts;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Billing.IntegrationTests;

[Collection(BillingApiTestGroup.Name)]
public sealed class InvoiceClosureIntegrationTests(BillingApiFixture fixture)
{
    [Fact]
    public async Task Concurrent_closures_reuse_one_attempt_and_debit_once()
    {
        fixture.Inventory.Reset();
        var productId = Guid.NewGuid();
        fixture.Inventory.AddProduct(productId, "CON-01", "Produto concorrente", 2);
        fixture.Inventory.WaitForConcurrentDebitRequests(2);
        var invoice = await CreateInvoiceAsync(productId, 1);

        var responses = await Task.WhenAll(
            CloseAsync(invoice.Id),
            CloseAsync(invoice.Id));

        try
        {
            Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        }
        finally
        {
            foreach (var response in responses) response.Dispose();
        }

        await using var db = await CreateDbContextAsync();
        var persisted = await db.Invoices.AsNoTracking().SingleAsync(item => item.Id == invoice.Id);
        var attempts = await db.ClosureAttempts.AsNoTracking()
            .Where(item => item.InvoiceId == invoice.Id)
            .ToListAsync();

        var attempt = Assert.Single(attempts);
        Assert.Equal(InvoiceStatus.Closed, persisted.Status);
        Assert.Equal(ClosureAttemptState.Completed, attempt.State);
        Assert.Equal(2, fixture.Inventory.DebitPostCount);
        Assert.Equal(1, fixture.Inventory.DebitSideEffectCount);
        Assert.Equal(1, fixture.Inventory.GetBalance(productId));
        Assert.Equal([attempt.Id], fixture.Inventory.PostAttemptIds.Distinct());
    }

    [Fact]
    public async Task Lost_result_is_reconciled_by_querying_the_same_attempt_key()
    {
        fixture.Inventory.Reset();
        var productId = Guid.NewGuid();
        fixture.Inventory.AddProduct(productId, "REC-01", "Produto reconciliado", 3);
        fixture.Inventory.HideNextCompletedDebitResult();
        var invoice = await CreateInvoiceAsync(productId, 2);

        using var firstResponse = await CloseAsync(invoice.Id);

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        InvoiceClosureAttempt attempt;
        await using (var db = await CreateDbContextAsync())
        {
            attempt = await db.ClosureAttempts.AsNoTracking()
                .SingleAsync(item => item.InvoiceId == invoice.Id);
            Assert.Equal(ClosureAttemptState.Pending, attempt.State);
        }

        ClosureResult result;
        using (var scope = fixture.Services.CreateScope())
        {
            var coordinator = scope.ServiceProvider.GetRequiredService<ClosureCoordinator>();
            result = await coordinator.ProcessAsync(attempt.Id, false, CancellationToken.None);
        }

        Assert.Equal(ClosureAttemptState.Completed, result.State);
        await using (var db = await CreateDbContextAsync())
        {
            var persistedInvoice = await db.Invoices.AsNoTracking().SingleAsync(item => item.Id == invoice.Id);
            var persistedAttempt = await db.ClosureAttempts.AsNoTracking().SingleAsync(item => item.Id == attempt.Id);
            Assert.Equal(InvoiceStatus.Closed, persistedInvoice.Status);
            Assert.Equal(ClosureAttemptState.Completed, persistedAttempt.State);
        }

        Assert.Equal(1, fixture.Inventory.DebitPostCount);
        Assert.Equal(1, fixture.Inventory.DebitGetCount);
        Assert.Equal(1, fixture.Inventory.DebitSideEffectCount);
        Assert.Equal(1, fixture.Inventory.GetBalance(productId));
        Assert.Equal([attempt.Id], fixture.Inventory.PostAttemptIds);
        Assert.Equal([attempt.Id], fixture.Inventory.GetAttemptIds);
    }

    [Fact]
    public async Task Open_invoice_can_be_edited_once_and_records_actor_before_closure()
    {
        fixture.Inventory.Reset();
        var firstProduct = Guid.NewGuid();
        var replacementProduct = Guid.NewGuid();
        fixture.Inventory.AddProduct(firstProduct, "EDT-01", "Produto inicial", 5);
        fixture.Inventory.AddProduct(replacementProduct, "EDT-02", "Produto editado", 5);
        var created = await CreateInvoiceAsync(firstProduct, 1);

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/invoices/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateInvoiceRequest([new CreateInvoiceItemRequest(replacementProduct, 2)]))
        };
        request.Headers.TryAddWithoutValidation("If-Match", $"\"{created.Version}\"");
        using var response = await fixture.Client.SendAsync(request);
        response.EnsureSuccessStatusCode();
        var updated = await response.Content.ReadFromJsonAsync<InvoiceResponse>()
            ?? throw new InvalidOperationException("Billing returned an empty edited invoice.");

        Assert.NotEqual(created.Version, updated.Version);
        Assert.Equal("Operador de Faturamento", updated.UpdatedBy);
        Assert.Contains(updated.AuditEvents, entry => entry.Type == "Edited" && entry.ActorName == "Operador de Faturamento");
        var item = Assert.Single(updated.Items);
        Assert.Equal(replacementProduct, item.ProductId);
        Assert.Equal(2, item.Quantity);

        using var stale = new HttpRequestMessage(HttpMethod.Put, $"/api/invoices/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateInvoiceRequest([new CreateInvoiceItemRequest(firstProduct, 1)]))
        };
        stale.Headers.TryAddWithoutValidation("If-Match", $"\"{created.Version}\"");
        using var staleResponse = await fixture.Client.SendAsync(stale);
        Assert.Equal(HttpStatusCode.Conflict, staleResponse.StatusCode);

        using var closeResponse = await CloseAsync(created.Id);
        closeResponse.EnsureSuccessStatusCode();
        using var afterClose = new HttpRequestMessage(HttpMethod.Put, $"/api/invoices/{created.Id}")
        {
            Content = JsonContent.Create(new UpdateInvoiceRequest([new CreateInvoiceItemRequest(firstProduct, 1)]))
        };
        afterClose.Headers.TryAddWithoutValidation("If-Match", $"\"{updated.Version}\"");
        using var afterCloseResponse = await fixture.Client.SendAsync(afterClose);
        Assert.Equal(HttpStatusCode.Conflict, afterCloseResponse.StatusCode);
        Assert.Equal(3, fixture.Inventory.GetBalance(replacementProduct));
    }

    private async Task<InvoiceResponse> CreateInvoiceAsync(Guid productId, int quantity)
    {
        using var response = await fixture.Client.PostAsJsonAsync(
            "/api/invoices/",
            new CreateInvoiceRequest([new CreateInvoiceItemRequest(productId, quantity)]));
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<InvoiceResponse>()
            ?? throw new InvalidOperationException("Billing returned an empty invoice response.");
    }

    private Task<HttpResponseMessage> CloseAsync(Guid invoiceId) =>
        fixture.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, $"/api/invoices/{invoiceId}/close"));

    private async Task<BillingDbContext> CreateDbContextAsync()
    {
        using var scope = fixture.Services.CreateScope();
        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<BillingDbContext>>();
        return await factory.CreateDbContextAsync();
    }
}
