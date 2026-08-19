using System.Globalization;
using Billing.Api.Domain;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Billing.Api.Application;

public interface IInvoicePdfGenerator
{
    byte[] Generate(Invoice invoice);
}

public sealed class InvoicePdfGenerator : IInvoicePdfGenerator
{
    public byte[] Generate(Invoice invoice)
    {
        if (invoice.Status != InvoiceStatus.Closed)
            throw new ConflictException("INVOICE_NOT_CLOSED", "O PDF só está disponível para uma nota fechada.");

        return Document.Create(document =>
        {
            document.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(x => x.FontSize(10));
                page.Header().Column(column =>
                {
                    column.Item().Text($"Nota #{invoice.Number}").Bold().FontSize(20);
                    column.Item().Text("Documento demonstrativo, sem validade fiscal").FontColor(Colors.Red.Medium);
                });
                page.Content().PaddingVertical(20).Column(column =>
                {
                    column.Spacing(12);
                    column.Item().Text($"Fechada em: {invoice.ClosedAt:dd/MM/yyyy HH:mm} UTC");
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.ConstantColumn(90);
                            columns.RelativeColumn();
                            columns.ConstantColumn(70);
                        });
                        table.Header(header =>
                        {
                            header.Cell().Element(HeaderCell).Text("Código");
                            header.Cell().Element(HeaderCell).Text("Descrição");
                            header.Cell().Element(HeaderCell).AlignRight().Text("Quantidade");
                        });
                        foreach (var item in invoice.Items.OrderBy(x => x.ProductCode))
                        {
                            table.Cell().Element(BodyCell).Text(item.ProductCode);
                            table.Cell().Element(BodyCell).Text(item.ProductDescription);
                            table.Cell().Element(BodyCell).AlignRight().Text(item.Quantity.ToString(CultureInfo.InvariantCulture));
                        }
                    });
                });
                page.Footer().AlignCenter().Text(text =>
                {
                    text.Span("NotaFlow • página ");
                    text.CurrentPageNumber();
                });
            });
        }).GeneratePdf();
    }

    private static IContainer HeaderCell(IContainer container) =>
        container.Background(Colors.Grey.Lighten2).Padding(6).DefaultTextStyle(x => x.SemiBold());

    private static IContainer BodyCell(IContainer container) =>
        container.BorderBottom(1).BorderColor(Colors.Grey.Lighten2).Padding(6);
}
