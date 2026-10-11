using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Nooks.Data.Migrations;

/// <inheritdoc />
public partial class _20261011124806_KeptChanges : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "added_lines",
            schema: "nooks",
            table: "checkpoints",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "changed_files",
            schema: "nooks",
            table: "checkpoints",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "deleted_lines",
            schema: "nooks",
            table: "checkpoints",
            type: "integer",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "added_lines",
            schema: "nooks",
            table: "checkpoints");

        migrationBuilder.DropColumn(
            name: "changed_files",
            schema: "nooks",
            table: "checkpoints");

        migrationBuilder.DropColumn(
            name: "deleted_lines",
            schema: "nooks",
            table: "checkpoints");
    }
}
