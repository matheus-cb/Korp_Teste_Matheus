using Billing.Api.Application;
using Billing.Api.Domain;

namespace Billing.Api.Tests;

public sealed class InvoicePdfGeneratorTests
{
    [Fact]
    public void Generates_pdf_only_after_invoice_is_closed()
    {
        QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;
        var invoice = Invoice.Create([new(Guid.NewGuid(), "P-1", "Produto", 2)], TimeProvider.System);
        var generator = new InvoicePdfGenerator();
        Assert.Throws<ConflictException>(() => generator.Generate(invoice));

        invoice.Close(DateTimeOffset.UtcNow);
        var bytes = generator.Generate(invoice);

        Assert.True(bytes.Length > 100);
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
    }
}
