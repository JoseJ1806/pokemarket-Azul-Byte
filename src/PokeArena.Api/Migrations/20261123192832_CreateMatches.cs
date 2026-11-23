using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokeArena.Api.Migrations
{
    /// <inheritdoc />
    public partial class CreateMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "matches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallengerPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    OpponentPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    LocationId = table.Column<int>(type: "integer", nullable: false),
                    ChallengerDeckCardIds = table.Column<string>(type: "jsonb", nullable: false),
                    OpponentDeckCardIds = table.Column<string>(type: "jsonb", nullable: false),
                    Status = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    CurrentTurnPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_matches", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_matches_ChallengerPlayerId_OpponentPlayerId",
                table: "matches",
                columns: new[] { "ChallengerPlayerId", "OpponentPlayerId" });

            migrationBuilder.CreateIndex(
                name: "IX_matches_IdempotencyKey",
                table: "matches",
                column: "IdempotencyKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "matches");
        }
    }
}
