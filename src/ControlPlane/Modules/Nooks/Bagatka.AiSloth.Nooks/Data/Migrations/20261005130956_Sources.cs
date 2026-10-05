using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Nooks.Data.Migrations;

/// <inheritdoc />
public partial class _20261005130956_Sources : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "copy_of",
            schema: "nooks",
            table: "nooks",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "created_by",
            schema: "nooks",
            table: "nooks",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "sources_ready",
            schema: "nooks",
            table: "nooks",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.CreateTable(
            name: "source_copies",
            schema: "nooks",
            columns: table => new
            {
                nook_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                repository_id = table.Column<Guid>(type: "uuid", nullable: false),
                branch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                commit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_source_copies", x => new { x.nook_id, x.name });
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "source_copies",
            schema: "nooks");

        migrationBuilder.DropColumn(
            name: "copy_of",
            schema: "nooks",
            table: "nooks");

        migrationBuilder.DropColumn(
            name: "created_by",
            schema: "nooks",
            table: "nooks");

        migrationBuilder.DropColumn(
            name: "sources_ready",
            schema: "nooks",
            table: "nooks");
    }
}
