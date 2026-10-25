using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PokeArena.Api.Migrations
{
    /// <inheritdoc />
    public partial class CreateLocations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "locations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ThumbnailUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    ImageStatus = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    IsAvailable = table.Column<bool>(type: "boolean", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CardLimit = table.Column<int>(type: "integer", nullable: false),
                    EnergyMode = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    EnergyAmount = table.Column<int>(type: "integer", nullable: false),
                    EffectType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    EffectParameters = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_locations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_locations_IdempotencyKey",
                table: "locations",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_locations_Name",
                table: "locations",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "locations");
        }
    }
}
