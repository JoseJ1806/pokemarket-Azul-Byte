using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

// MIGRACIÓN DEL SPRINT 2: crea la tabla player_accounts (US Inicio de sesión con proveedor).
// La generó Entity Framework con "dotnet ef migrations add AddPlayerAccounts" a partir de
// la configuración de PlayerAccount en PokeArenaDbContext. Solo se agregó este comentario.
// Up crea la tabla y el índice único (Provider, ProviderSubject) que impide cuentas
// duplicadas. Down la elimina si se revierte la migración.
namespace PokeArena.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "player_accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderSubject = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                    Email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: false),
                    Role = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    Elo = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_player_accounts", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_player_accounts_Provider_ProviderSubject",
                table: "player_accounts",
                columns: new[] { "Provider", "ProviderSubject" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "player_accounts");
        }
    }
}
