using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Api.Infrastructure.Migrations;

/// <summary>Itens ignorados na baixa, propagados do Inventory (INV-04).</summary>
[DbContext(typeof(BillingDbContext))]
[Migration("202608180003_AddClosureIgnoredItems")]
public sealed class AddClosureIgnoredItems : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.AddColumn<string>(
            name: "IgnoredItemsJson",
            table: "invoice_closure_attempts",
            type: "jsonb",
            nullable: true);

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropColumn(name: "IgnoredItemsJson", table: "invoice_closure_attempts");
}
