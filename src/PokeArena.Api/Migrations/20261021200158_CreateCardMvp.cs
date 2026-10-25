using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokeArena.Api.Migrations
{
    /// <inheritdoc />
    public partial class CreateCardMvp : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "IdempotencyKey",
                table: "cards",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsAvailable",
                table: "cards",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailUrl",
                table: "cards",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "abilities",
                columns: table => new
                {
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Trigger = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Effect = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsEvolution = table.Column<bool>(type: "boolean", nullable: false),
                    TargetCardCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_abilities", x => x.Code);
                });

            migrationBuilder.InsertData(
                table: "abilities",
                columns: new[] { "Code", "Effect", "IsEvolution", "Name", "TargetCardCode", "Trigger" },
                values: new object[] { "HAB-000", null, false, "Sin Habilidad", null, null });

            migrationBuilder.CreateIndex(
                name: "IX_cards_IdempotencyKey",
                table: "cards",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "abilities");

            migrationBuilder.DropIndex(
                name: "IX_cards_IdempotencyKey",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "IdempotencyKey",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "IsAvailable",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "ThumbnailUrl",
                table: "cards");
        }
    }
}
