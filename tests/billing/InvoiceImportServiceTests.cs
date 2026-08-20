using System.Text;
using Billing.Api.Application;
using Billing.Api.Domain;
using Billing.Api.Infrastructure;

namespace Billing.Api.Tests;

public sealed class InvoiceImportServiceTests
{
    [Fact]
    public async Task Close_after_import_reports_rejected_stock_debit_to_the_operator()
    {
        var (service, inventory) = CreateService();
        inventory.DebitResults.Enqueue(new StockDebitOutcome(
            "Rejected",
            "INSUFFICIENT_STOCK",
            "Sem saldo para fechar a nota."));

        await using var csv = Csv("nota;codigo;quantidade\n1;CABO-USB;2\n");
        var result = await service.ImportAsync(csv, "notas.csv", true, CancellationToken.None);

        Assert.Equal(1, result.CreatedInvoices);
        var error = Assert.Single(result.Errors);
        Assert.Equal("INSUFFICIENT_STOCK", error.Code);
        Assert.Contains("Sem saldo", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Close_after_import_reports_pending_closure_to_the_operator()
    {
        var (service, inventory) = CreateService();
        inventory.DebitResults.Enqueue(new DependencyUnavailableException("INVENTORY_UNAVAILABLE", "offline"));

        await using var csv = Csv("nota;codigo;quantidade\n1;CABO-USB;2\n");
        var result = await service.ImportAsync(csv, "notas.csv", true, CancellationToken.None);

        Assert.Equal(1, result.CreatedInvoices);
        var error = Assert.Single(result.Errors);
        Assert.Equal("INVENTORY_UNAVAILABLE", error.Code);
        Assert.Contains("ainda está sendo confirmado", error.Message, StringComparison.Ordinal);
    }

    private static (InvoiceImportService Service, FakeInventoryClient Inventory) CreateService()
    {
        var factory = new InMemoryBillingDbFactory(Guid.NewGuid().ToString());
        var inventory = new FakeInventoryClient();
        var product = new InventoryProduct(Guid.NewGuid(), "CABO-USB", "Cabo USB", 10);
        inventory.Products[product.Id] = product;

        var context = factory.CreateDbContext();
        var clock = TimeProvider.System;
        var httpContext = TestHttpContext.For("Ana Operadora");
        var invoices = new InvoiceService(context, inventory, clock, httpContext);
        var closures = new ClosureCoordinator(
            factory,
            inventory,
            clock,
            httpContext,
            TestLoggers.For<ClosureCoordinator>());
        return (
            new InvoiceImportService(context, inventory, invoices, closures, clock, httpContext),
            inventory);
    }

    private static MemoryStream Csv(string content) => new(Encoding.UTF8.GetBytes(content));
}
