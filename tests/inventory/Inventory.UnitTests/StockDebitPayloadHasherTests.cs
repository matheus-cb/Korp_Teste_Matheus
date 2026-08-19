using Inventory.Api.Application;
using Inventory.Api.Contracts;

namespace Inventory.UnitTests;

public sealed class StockDebitPayloadHasherTests
{
    [Fact]
    public void ComputeIsStableWhenItemOrderChanges()
    {
        var invoiceId = Guid.NewGuid();
        var firstProduct = Guid.NewGuid();
        var secondProduct = Guid.NewGuid();
        var first = new StockDebitRequest(invoiceId,
        [
            new StockDebitItemRequest(firstProduct, 2),
            new StockDebitItemRequest(secondProduct, 3)
        ]);
        var reordered = new StockDebitRequest(invoiceId,
        [
            new StockDebitItemRequest(secondProduct, 3),
            new StockDebitItemRequest(firstProduct, 2)
        ]);

        Assert.Equal(StockDebitPayloadHasher.Compute(first), StockDebitPayloadHasher.Compute(reordered));
    }

    [Fact]
    public void ComputeChangesWhenInvoiceProductOrQuantityChanges()
    {
        var invoiceId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var baseline = new StockDebitRequest(invoiceId, [new StockDebitItemRequest(productId, 2)]);
        var otherInvoice = baseline with { InvoiceId = Guid.NewGuid() };
        var otherProduct = baseline with
        {
            Items = [new StockDebitItemRequest(Guid.NewGuid(), 2)]
        };
        var otherQuantity = baseline with
        {
            Items = [new StockDebitItemRequest(productId, 3)]
        };

        var baselineHash = StockDebitPayloadHasher.Compute(baseline);
        Assert.NotEqual(baselineHash, StockDebitPayloadHasher.Compute(otherInvoice));
        Assert.NotEqual(baselineHash, StockDebitPayloadHasher.Compute(otherProduct));
        Assert.NotEqual(baselineHash, StockDebitPayloadHasher.Compute(otherQuantity));
    }
}
