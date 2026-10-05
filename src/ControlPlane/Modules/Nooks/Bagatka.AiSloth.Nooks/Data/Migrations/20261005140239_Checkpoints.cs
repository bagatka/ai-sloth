using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Nooks.Data.Migrations;

/// <inheritdoc />
public partial class _20261005140239_Checkpoints : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "copy_checkpoint",
            schema: "nooks",
            table: "nooks",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<List<string>>(
            name: "kept_paths",
            schema: "nooks",
            table: "nooks",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'");

        migrationBuilder.CreateTable(
            name: "checkpoints",
            schema: "nooks",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                nook_id = table.Column<Guid>(type: "uuid", nullable: false),
                number = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_checkpoints", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "checkpoint_parts",
            schema: "nooks",
            columns: table => new
            {
                checkpoint_id = table.Column<Guid>(type: "uuid", nullable: false),
                path = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                commit = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                previous = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                object_key = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_checkpoint_parts", x => new { x.checkpoint_id, x.path });
                table.ForeignKey(
                    name: "fk_checkpoint_parts_checkpoints_checkpoint_id",
                    column: x => x.checkpoint_id,
                    principalSchema: "nooks",
                    principalTable: "checkpoints",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_checkpoints_nook_id_id",
            schema: "nooks",
            table: "checkpoints",
            columns: new[] { "nook_id", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_checkpoints_nook_id_number",
            schema: "nooks",
            table: "checkpoints",
            columns: new[] { "nook_id", "number" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "checkpoint_parts",
            schema: "nooks");

        migrationBuilder.DropTable(
            name: "checkpoints",
            schema: "nooks");

        migrationBuilder.DropColumn(
            name: "copy_checkpoint",
            schema: "nooks",
            table: "nooks");

        migrationBuilder.DropColumn(
            name: "kept_paths",
            schema: "nooks",
            table: "nooks");
    }
}
