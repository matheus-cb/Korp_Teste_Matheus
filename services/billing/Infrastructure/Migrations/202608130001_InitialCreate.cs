using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Api.Infrastructure.Migrations;

[DbContext(typeof(BillingDbContext))]
[Migration("202608130001_InitialCreate")]
public partial class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateSequence<long>(name: "invoice_number_seq");
        migrationBuilder.CreateTable(
            name: "ai_draft_runs",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                Model = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                PromptVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ToolNames = table.Column<string>(type: "jsonb", nullable: false),
                InputTokens = table.Column<int>(type: "integer", nullable: false),
                OutputTokens = table.Column<int>(type: "integer", nullable: false),
                EstimatedCostUsd = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: false),
                DurationMilliseconds = table.Column<long>(type: "bigint", nullable: false),
                FailureCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_ai_draft_runs", x => x.Id));

        migrationBuilder.CreateTable(
            name: "invoices",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Number = table.Column<long>(type: "bigint", nullable: false, defaultValueSql: "nextval('invoice_number_seq')"),
                Status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_invoices", x => x.Id));

        migrationBuilder.CreateTable(
            name: "invoice_closure_attempts",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                PayloadHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                RetryCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                NextRetryAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Version = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invoice_closure_attempts", x => x.Id);
                table.ForeignKey("FK_invoice_closure_attempts_invoices_InvoiceId", x => x.InvoiceId, "invoices", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "invoice_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ProductDescription = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_invoice_items", x => x.Id);
                table.CheckConstraint("ck_invoice_items_quantity", "\"Quantity\" > 0");
                table.ForeignKey("FK_invoice_items_invoices_InvoiceId", x => x.InvoiceId, "invoices", "Id", onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex("IX_invoice_closure_attempts_InvoiceId", "invoice_closure_attempts", "InvoiceId", unique: true, filter: "\"State\" = 'Pending'");
        migrationBuilder.CreateIndex("IX_invoice_closure_attempts_State_NextRetryAt", "invoice_closure_attempts", new[] { "State", "NextRetryAt" });
        migrationBuilder.CreateIndex("IX_invoice_items_InvoiceId_ProductId", "invoice_items", new[] { "InvoiceId", "ProductId" }, unique: true);
        migrationBuilder.CreateIndex("IX_invoices_Number", "invoices", "Number", unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("ai_draft_runs");
        migrationBuilder.DropTable("invoice_closure_attempts");
        migrationBuilder.DropTable("invoice_items");
        migrationBuilder.DropTable("invoices");
        migrationBuilder.DropSequence("invoice_number_seq");
    }
}
