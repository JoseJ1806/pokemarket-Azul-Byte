using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PokeArena.Api.Migrations
{
    /// <inheritdoc />
    public partial class CreateCollection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "card_copies",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<int>(type: "integer", nullable: false),
                    Origin = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    AcquiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_card_copies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "cube_balances",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Balance = table.Column<int>(type: "integer", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cube_balances", x => x.PlayerId);
                    table.CheckConstraint("CK_cube_balances_Balance_NotNegative", "\"Balance\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "cube_movements",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Amount = table.Column<int>(type: "integer", nullable: false),
                    Reason = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cube_movements", x => x.Id);
                    table.CheckConstraint("CK_cube_movements_Amount_NotZero", "\"Amount\" <> 0");
                });

            migrationBuilder.CreateTable(
                name: "initial_deliveries",
                columns: table => new
                {
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_initial_deliveries", x => x.PlayerId);
                });

            migrationBuilder.CreateIndex(
                name: "IX_card_copies_PlayerId_CardId",
                table: "card_copies",
                columns: new[] { "PlayerId", "CardId" });

            migrationBuilder.CreateIndex(
                name: "IX_cube_movements_PlayerId_Id",
                table: "cube_movements",
                columns: new[] { "PlayerId", "Id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "card_copies");

            migrationBuilder.DropTable(
                name: "cube_balances");

            migrationBuilder.DropTable(
                name: "cube_movements");

            migrationBuilder.DropTable(
                name: "initial_deliveries");
        }
    }
}
