using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Nooks.Data.Migrations;

/// <inheritdoc />
public partial class _20261004164339_AddNookHarness : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "harness",
            schema: "nooks",
            table: "nooks",
            type: "character varying(32)",
            maxLength: 32,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "harness",
            schema: "nooks",
            table: "nooks");
    }
}
