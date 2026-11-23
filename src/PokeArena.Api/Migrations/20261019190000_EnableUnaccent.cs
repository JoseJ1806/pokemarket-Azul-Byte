using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using PokeArena.Api.Data;

#nullable disable

namespace PokeArena.Api.Migrations;

// MIGRACIÓN DE BASE DE DATOS.
// Se ejecuta sobre PostgreSQL antes de que Program.cs haga búsquedas sin tildes.
[DbContext(typeof(PokeArenaDbContext))]
[Migration("20261019190000_EnableUnaccent")]
public partial class EnableUnaccent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Habilita la función PostgreSQL que permite buscar sin distinguir tildes.
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS unaccent;");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // No se elimina: la extensión podría ser utilizada por otras consultas.
    }
}