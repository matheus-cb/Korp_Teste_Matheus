using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Api.Infrastructure.Migrations;

/// <summary>Importação de notas por CSV, com idempotência por conteúdo.</summary>
[DbContext(typeof(BillingDbContext))]
[Migration("202608190001_AddInvoiceImports")]
public sealed class AddInvoiceImports : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "invoice_imports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                FileName = table.Column<string>(type: "character varying(260)", maxLength: 260, nullable: false),
                ContentHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                CreatedInvoices = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_invoice_imports", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "ux_invoice_imports_hash",
            table: "invoice_imports",
            column: "ContentHash",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable(name: "invoice_imports");
}
