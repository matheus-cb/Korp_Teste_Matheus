using Billing.Api.Application;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;

namespace Billing.Api.Tests;

public sealed class ClosureCoordinatorTests
{
    [Fact]
    public async Task Completed_debit_closes_invoice_and_attempt()
    {
        var (factory, invoice, attempt) = await SeedAsync();
        var inventory = new FakeInventoryClient();
        inventory.DebitResults.Enqueue(new StockDebitOutcome("Completed"));
        var coordinator = CreateCoordinator(factory, inventory);

        var result = await coordinator.ProcessAsync(attempt.Id, true, CancellationToken.None);

        Assert.Equal(ClosureAttemptState.Completed, result.State);
        await using var verification = factory.CreateDbContext();
        Assert.Equal(InvoiceStatus.Closed, (await verification.Invoices.FindAsync(invoice.Id))!.Status);
        Assert.Equal(ClosureAttemptState.Completed, (await verification.ClosureAttempts.FindAsync(attempt.Id))!.State);
    }

    [Fact]
    public async Task Lost_response_remains_pending_then_reconciles_without_new_key()
    {
        var (factory, invoice, attempt) = await SeedAsync();
        var inventory = new FakeInventoryClient();
        inventory.DebitResults.Enqueue(new DependencyUnavailableException("INVENTORY_UNAVAILABLE", "offline"));
        inventory.QueryResults.Enqueue(new StockDebitOutcome("Completed"));
        var coordinator = CreateCoordinator(factory, inventory);

        var first = await coordinator.ProcessAsync(attempt.Id, true, CancellationToken.None);
        var second = await coordinator.ProcessAsync(attempt.Id, false, CancellationToken.None);

        Assert.Equal(ClosureAttemptState.Pending, first.State);
        Assert.Equal(ClosureAttemptState.Completed, second.State);
        Assert.Equal(1, inventory.DebitCalls);
        Assert.Equal(1, inventory.QueryCalls);
        await using var verification = factory.CreateDbContext();
        Assert.Equal(InvoiceStatus.Closed, (await verification.Invoices.FindAsync(invoice.Id))!.Status);
    }

    [Fact]
    public async Task Rejected_debit_keeps_invoice_open()
    {
        var (factory, invoice, attempt) = await SeedAsync();
        var inventory = new FakeInventoryClient();
        inventory.DebitResults.Enqueue(new StockDebitOutcome("Rejected", "INSUFFICIENT_STOCK", "Sem saldo"));

        var result = await CreateCoordinator(factory, inventory).ProcessAsync(attempt.Id, true, CancellationToken.None);

        Assert.Equal(ClosureAttemptState.Rejected, result.State);
        await using var verification = factory.CreateDbContext();
        Assert.Equal(InvoiceStatus.Open, (await verification.Invoices.FindAsync(invoice.Id))!.Status);
    }

    private static ClosureCoordinator CreateCoordinator(InMemoryBillingDbFactory factory, FakeInventoryClient inventory) =>
        new(
            factory,
            inventory,
            TimeProvider.System,
            TestHttpContext.For("Ana Operadora"),
            TestLoggers.For<ClosureCoordinator>());

    private static async Task<(InMemoryBillingDbFactory Factory, Invoice Invoice, InvoiceClosureAttempt Attempt)> SeedAsync()
    {
        var factory = new InMemoryBillingDbFactory(Guid.NewGuid().ToString());
        var invoice = Invoice.Create([new(Guid.NewGuid(), "P-1", "Produto", 1)], TimeProvider.System);
        var attempt = InvoiceClosureAttempt.Start(
            invoice.Id,
            InvoiceService.ComputePayloadHash(invoice.Items),
            DateTimeOffset.UtcNow);
        await using var db = factory.CreateDbContext();
        db.Invoices.Add(invoice);
        db.ClosureAttempts.Add(attempt);
        await db.SaveChangesAsync();
        return (factory, invoice, attempt);
    }
}
