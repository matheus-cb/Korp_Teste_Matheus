using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Api.Infrastructure.Migrations;

[DbContext(typeof(BillingDbContext))]
[Migration("202608200001_AddInvoiceEditingAudit")]
public sealed class AddInvoiceEditingAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(name: "UpdatedAt", table: "invoices", type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP");
        migrationBuilder.AddColumn<string>(name: "UpdatedBy", table: "invoices", type: "character varying(120)", maxLength: 120, nullable: false, defaultValue: "sistema");
        migrationBuilder.CreateTable(name: "invoice_audit_events", columns: table => new
        {
            Id = table.Column<Guid>(type: "uuid", nullable: false),
            InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
            Type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
            ActorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
            OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
        }, constraints: table =>
        {
            table.PrimaryKey("PK_invoice_audit_events", x => x.Id);
            table.ForeignKey("FK_invoice_audit_events_invoices_InvoiceId", x => x.InvoiceId, "invoices", "Id", onDelete: ReferentialAction.Cascade);
        });
        migrationBuilder.CreateIndex(name: "IX_invoice_audit_events_InvoiceId_OccurredAt", table: "invoice_audit_events", columns: new[] { "InvoiceId", "OccurredAt" });
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "invoice_audit_events");
        migrationBuilder.DropColumn(name: "UpdatedAt", table: "invoices");
        migrationBuilder.DropColumn(name: "UpdatedBy", table: "invoices");
    }
}
