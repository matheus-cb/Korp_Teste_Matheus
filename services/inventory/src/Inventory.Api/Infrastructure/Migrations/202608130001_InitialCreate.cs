using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Inventory.Api.Infrastructure.Migrations;

[DbContext(typeof(InventoryDbContext))]
[Migration("202608130001_InitialCreate")]
public sealed class InitialCreate : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Products",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                Code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Description = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                Balance = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_Products", x => x.Id);
                table.CheckConstraint("CK_Products_Balance", "\"Balance\" >= 0");
            });

        migrationBuilder.CreateTable(
            name: "StockDebitOperations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                AttemptId = table.Column<Guid>(type: "uuid", nullable: false),
                InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                RequestHash = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                State = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                ErrorCode = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                FinishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table => table.PrimaryKey("PK_StockDebitOperations", x => x.Id));

        migrationBuilder.CreateTable(
            name: "StockMovements",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                OperationId = table.Column<Guid>(type: "uuid", nullable: false),
                ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                Quantity = table.Column<int>(type: "integer", nullable: false),
                BalanceBefore = table.Column<int>(type: "integer", nullable: false),
                BalanceAfter = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_StockMovements", x => x.Id);
                table.CheckConstraint("CK_StockMovements_Balances", "\"BalanceBefore\" >= 0 AND \"BalanceAfter\" >= 0 AND \"BalanceAfter\" = \"BalanceBefore\" - \"Quantity\"");
                table.CheckConstraint("CK_StockMovements_Quantity", "\"Quantity\" > 0");
                table.ForeignKey(
                    name: "FK_StockMovements_Products_ProductId",
                    column: x => x.ProductId,
                    principalTable: "Products",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_StockMovements_StockDebitOperations_OperationId",
                    column: x => x.OperationId,
                    principalTable: "StockDebitOperations",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "UX_Products_Code",
            table: "Products",
            column: "Code",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "UX_StockDebitOperations_AttemptId",
            table: "StockDebitOperations",
            column: "AttemptId",
            unique: true);
        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_OperationId",
            table: "StockMovements",
            column: "OperationId");
        migrationBuilder.CreateIndex(
            name: "IX_StockMovements_ProductId",
            table: "StockMovements",
            column: "ProductId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "StockMovements");
        migrationBuilder.DropTable(name: "Products");
        migrationBuilder.DropTable(name: "StockDebitOperations");
    }
}
