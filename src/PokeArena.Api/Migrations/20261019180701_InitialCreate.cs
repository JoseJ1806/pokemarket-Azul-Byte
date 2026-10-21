using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PokeArena.Api.Migrations
{
    // Primera versión del esquema que almacena las cartas.
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        // Crea la tabla inicial de cartas y el índice para búsquedas por nombre.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cards",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SetCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CardNumber = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ImageUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_cards_Name",
                table: "cards",
                column: "Name");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revierte la migración eliminando la tabla creada por Up.
            migrationBuilder.DropTable(
                name: "cards");
        }
    }
}
