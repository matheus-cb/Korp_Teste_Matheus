using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Billing.Api.Infrastructure.Migrations;

/// <summary>
/// Identidade do operador e autoria das notas. A identidade vive no Billing,
/// que já é dono das notas; o Inventory recebe o operador propagado.
/// </summary>
[DbContext(typeof(BillingDbContext))]
[Migration("202608180002_AddUsersAndAuthorship")]
public sealed class AddUsersAndAuthorship : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "users",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DisplayName = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                PasswordHash = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_users", x => x.Id));

        migrationBuilder.CreateIndex(
            name: "ux_users_username",
            table: "users",
            column: "UserName",
            unique: true);

        migrationBuilder.CreateTable(
            name: "user_sessions",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                UserId = table.Column<Guid>(type: "uuid", nullable: false),
                TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_user_sessions", x => x.Id);
                table.ForeignKey(
                    name: "FK_user_sessions_users_UserId",
                    column: x => x.UserId,
                    principalTable: "users",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ux_user_sessions_token",
            table: "user_sessions",
            column: "TokenHash",
            unique: true);

        migrationBuilder.AddColumn<string>(
            name: "CreatedBy",
            table: "invoices",
            type: "character varying(120)",
            maxLength: 120,
            nullable: false,
            defaultValue: "sistema");

        migrationBuilder.AddColumn<string>(
            name: "ClosedBy",
            table: "invoices",
            type: "character varying(120)",
            maxLength: 120,
            nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(name: "user_sessions");
        migrationBuilder.DropTable(name: "users");
        migrationBuilder.DropColumn(name: "ClosedBy", table: "invoices");
        migrationBuilder.DropColumn(name: "CreatedBy", table: "invoices");
    }
}
