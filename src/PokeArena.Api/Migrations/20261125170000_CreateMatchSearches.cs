using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokeArena.Api.Migrations
{
    /// <inheritdoc />
    public partial class CreateMatchSearches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "match_searches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Context = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    DeckId = table.Column<Guid>(type: "uuid", nullable: false),
                    DeckCardIds = table.Column<string>(type: "jsonb", nullable: false),
                    Elo = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastSeenAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    PairingId = table.Column<Guid>(type: "uuid", nullable: true),
                    PairedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MatchId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClosedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Notice = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_match_searches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_match_searches_PairingId",
                table: "match_searches",
                column: "PairingId");

            migrationBuilder.CreateIndex(
                name: "IX_match_searches_PlayerId",
                table: "match_searches",
                column: "PlayerId",
                unique: true,
                filter: "\"Status\" IN ('Waiting', 'Pairing')");

            migrationBuilder.CreateIndex(
                name: "IX_match_searches_PlayerId_CreatedAt",
                table: "match_searches",
                columns: new[] { "PlayerId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_match_searches_Status_Context_Elo",
                table: "match_searches",
                columns: new[] { "Status", "Context", "Elo" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "match_searches");
        }
    }
}
