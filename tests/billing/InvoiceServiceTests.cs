using Billing.Api.Application;
using Billing.Api.Contracts;
using Billing.Api.Domain;

namespace Billing.Api.Tests;

public sealed class InvoiceServiceTests
{
    [Fact]
    public async Task Create_uses_authoritative_inventory_snapshot()
    {
        var factory = new InMemoryBillingDbFactory(Guid.NewGuid().ToString());
        var productId = Guid.NewGuid();
        var inventory = new FakeInventoryClient();
        inventory.Products[productId] = new(productId, "AUTH-01", "Descrição do estoque", 8);
        await using var db = factory.CreateDbContext();
        var service = new InvoiceService(db, inventory, TimeProvider.System, TestHttpContext.For("Ana Operadora"));

        var invoice = await service.CreateAsync(
            new CreateInvoiceRequest([new(productId, 2)]),
            CancellationToken.None);

        Assert.Equal("AUTH-01", Assert.Single(invoice.Items).ProductCode);
        Assert.Equal("Descrição do estoque", Assert.Single(invoice.Items).ProductDescription);
    }

    [Fact]
    public async Task Create_rejects_unknown_product()
    {
        var factory = new InMemoryBillingDbFactory(Guid.NewGuid().ToString());
        await using var db = factory.CreateDbContext();
        var service = new InvoiceService(
            db,
            new FakeInventoryClient(),
            TimeProvider.System,
            TestHttpContext.For("Ana Operadora"));

        var exception = await Assert.ThrowsAsync<ResourceNotFoundException>(() => service.CreateAsync(
            new CreateInvoiceRequest([new(Guid.NewGuid(), 1)]), CancellationToken.None));

        Assert.Equal("PRODUCT_NOT_FOUND", exception.Code);
    }
}
