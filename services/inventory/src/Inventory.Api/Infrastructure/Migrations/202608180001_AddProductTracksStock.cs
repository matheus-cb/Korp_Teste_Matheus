using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Infrastructure.Migrations;

/// <summary>
/// Controle de estoque por produto. Produtos existentes passam a controlar
/// estoque (default true), preservando o comportamento anterior.
/// </summary>
[DbContext(typeof(InventoryDbContext))]
[Migration("202608180001_AddProductTracksStock")]
public sealed class AddProductTracksStock : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "TracksStock",
            table: "Products",
            type: "boolean",
            nullable: false,
            defaultValue: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "TracksStock", table: "Products");
    }
}
