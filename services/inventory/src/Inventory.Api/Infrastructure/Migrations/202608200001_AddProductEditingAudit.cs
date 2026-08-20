using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Infrastructure.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("202608200001_AddProductEditingAudit")]
public sealed class AddProductEditingAudit : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(name: "CreatedBy", table: "Products", type: "character varying(120)", maxLength: 120, nullable: false, defaultValue: "sistema");
        migrationBuilder.AddColumn<DateTimeOffset>(name: "UpdatedAt", table: "Products", type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP");
        migrationBuilder.AddColumn<string>(name: "UpdatedBy", table: "Products", type: "character varying(120)", maxLength: 120, nullable: false, defaultValue: "sistema");
        migrationBuilder.AddColumn<Guid>(name: "Version", table: "Products", type: "uuid", nullable: false, defaultValueSql: "gen_random_uuid()");
    }
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "CreatedBy", table: "Products");
        migrationBuilder.DropColumn(name: "UpdatedAt", table: "Products");
        migrationBuilder.DropColumn(name: "UpdatedBy", table: "Products");
        migrationBuilder.DropColumn(name: "Version", table: "Products");
    }
}
