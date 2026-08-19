using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Infrastructure.Migrations;

/// <summary>
/// Itens ignorados na baixa (INV-04): produto sem controle de estoque entra na
/// nota mas não movimenta, e isso passa a ser reportado em vez de silencioso.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("202608180003_AddIgnoredItems")]
public sealed class AddIgnoredItems : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "IgnoredItemsJson",
            table: "StockDebitOperations",
            type: "jsonb",
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "IgnoredItemsJson", table: "StockDebitOperations");
    }
}
