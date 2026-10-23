using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PokeArena.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAbilityAffectedType : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AbilityAffectedType",
                table: "cards",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AffectedType",
                table: "abilities",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "AbilityAffectedType", table: "cards");
            migrationBuilder.DropColumn(name: "AffectedType", table: "abilities");
        }
    }
}
