using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokeArena.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddDecks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "decks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_decks", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "deck_cards",
                columns: table => new
                {
                    DeckId = table.Column<Guid>(type: "uuid", nullable: false),
                    CardId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_deck_cards", x => new { x.DeckId, x.CardId });
                    table.ForeignKey(
                        name: "FK_deck_cards_decks_DeckId",
                        column: x => x.DeckId,
                        principalTable: "decks",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_decks_PlayerId_IdempotencyKey",
                table: "decks",
                columns: new[] { "PlayerId", "IdempotencyKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_decks_PlayerId_Name",
                table: "decks",
                columns: new[] { "PlayerId", "Name" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "deck_cards");

            migrationBuilder.DropTable(
                name: "decks");
        }
    }
}
