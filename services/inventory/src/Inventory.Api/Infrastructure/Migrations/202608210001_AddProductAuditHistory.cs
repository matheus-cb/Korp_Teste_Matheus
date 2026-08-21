using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Infrastructure.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("202608210001_AddProductAuditHistory")]
public sealed class AddProductAuditHistory : Migration
{
    private static readonly string[] ProductAuditEventIndexColumns = ["ProductId", "OccurredAt"];

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ProductAuditEvents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                ActorName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ProductAuditEvents", x => x.Id);
                table.ForeignKey(
                    name: "FK_ProductAuditEvents_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ProductAuditEvents_ProductId_OccurredAt",
            table: "ProductAuditEvents",
            columns: ProductAuditEventIndexColumns);

        // Produtos anteriores à trilha continuam rastreáveis desde a criação;
        // não inventamos eventos de edição que o banco antigo não conhecia.
        migrationBuilder.Sql("""
            INSERT INTO "ProductAuditEvents" ("Id", "ProductId", "Type", "ActorName", "OccurredAt")
            SELECT gen_random_uuid(), "Id", 'Created', "CreatedBy", "CreatedAt"
            FROM "Products";
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "ProductAuditEvents");
    }
}
