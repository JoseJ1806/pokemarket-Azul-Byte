
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokeArena.Api.Migrations
{
    // Amplía la tabla inicial con los datos que necesita la búsqueda de cartas.
    /// <inheritdoc />
    public partial class ExpandCardForSearch : Migration
    {
        // Agrega los campos necesarios para filtrar y mostrar cartas completas.
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "cards",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Por definir");

            migrationBuilder.AddColumn<string>(
                name: "Rarity",
                table: "cards",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Por definir");

            migrationBuilder.AddColumn<string>(
                name: "AbilityCode",
                table: "cards",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AbilityEffect",
                table: "cards",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AbilityTrigger",
                table: "cards",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Code",
                table: "cards",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "Cost",
                table: "cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "ImageStatus",
                table: "cards",
                type: "character varying(30)",
                maxLength: 30,
                nullable: false,
                defaultValue: "Pendiente");

            migrationBuilder.AddColumn<int>(
                name: "Power",
                table: "cards",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.Sql("""
                UPDATE cards
                SET "Code" =
                    COALESCE(NULLIF("SetCode", ''), 'CARD')
                    || '-'
                    || COALESCE(NULLIF("CardNumber", ''), "Id"::text)
                    || '-'
                    || "Id"::text;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_cards_Code",
                table: "cards",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_cards_Cost_Power",
                table: "cards",
                columns: new[] { "Cost", "Power" });


        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revierte los índices y columnas agregados por esta migración.
            migrationBuilder.DropIndex(
                name: "IX_cards_Code",
                table: "cards");

            migrationBuilder.DropIndex(
                name: "IX_cards_Cost_Power",
                table: "cards");


            migrationBuilder.DropColumn(
                name: "AbilityCode",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "AbilityEffect",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "AbilityTrigger",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "Code",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "Cost",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "ImageStatus",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "Power",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "cards");

            migrationBuilder.DropColumn(
                name: "Rarity",
                table: "cards");
        }
    }
}
