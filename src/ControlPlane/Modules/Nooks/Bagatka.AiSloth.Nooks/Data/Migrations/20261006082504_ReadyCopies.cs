using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Nooks.Data.Migrations;

/// <inheritdoc />
public partial class _20261006082504_ReadyCopies : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "from_scratch",
            schema: "nooks",
            table: "nooks",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<bool>(
            name: "ready_copy_due",
            schema: "nooks",
            table: "nooks",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "ready_copy_made_at",
            schema: "nooks",
            table: "nooks",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "ready_copies",
            schema: "nooks",
            columns: table => new
            {
                match = table.Column<string>(type: "character(64)", fixedLength: true, maxLength: 64, nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                location = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                snapshot = table.Column<Guid>(type: "uuid", nullable: false),
                made_from = table.Column<Guid>(type: "uuid", nullable: false),
                made_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_ready_copies", x => x.match);
            });

        migrationBuilder.CreateIndex(
            name: "ix_ready_copies_used_at",
            schema: "nooks",
            table: "ready_copies",
            column: "used_at");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ready_copies",
            schema: "nooks");

        migrationBuilder.DropColumn(
            name: "from_scratch",
            schema: "nooks",
            table: "nooks");

        migrationBuilder.DropColumn(
            name: "ready_copy_due",
            schema: "nooks",
            table: "nooks");

        migrationBuilder.DropColumn(
            name: "ready_copy_made_at",
            schema: "nooks",
            table: "nooks");
    }
}
