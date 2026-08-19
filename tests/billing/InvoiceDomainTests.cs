using Billing.Api.Application;
using Billing.Api.Domain;

namespace Billing.Api.Tests;

public sealed class InvoiceDomainTests
{
    [Fact]
    public void Create_aggregates_duplicate_products_and_preserves_snapshot()
    {
        var productId = Guid.NewGuid();
        var invoice = Invoice.Create(
        [
            new(productId, "P-1", "Produto original", 2),
            new(productId, "P-1", "Produto original", 3)
        ], TimeProvider.System);

        var item = Assert.Single(invoice.Items);
        Assert.Equal(5, item.Quantity);
        Assert.Equal("Produto original", item.ProductDescription);
        Assert.Equal(InvoiceStatus.Open, invoice.Status);
    }

    [Fact]
    public void Create_rejects_non_positive_quantity()
    {
        Assert.Throws<DomainValidationException>(() => Invoice.Create(
            [new(Guid.NewGuid(), "P-1", "Produto", 0)], TimeProvider.System));
    }

    [Fact]
    public void Payload_hash_is_stable_independent_of_item_order()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var first = Invoice.Create(
            [new(firstId, "A", "A", 1), new(secondId, "B", "B", 2)], TimeProvider.System);
        var second = Invoice.Create(
            [new(secondId, "B", "B", 2), new(firstId, "A", "A", 1)], TimeProvider.System);

        Assert.Equal(
            InvoiceService.ComputePayloadHash(first.Items),
            InvoiceService.ComputePayloadHash(second.Items));
    }
}
