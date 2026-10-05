using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261005153006_CheckpointsStateAndInstructions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "checkpoint_after",
            schema: "chats",
            table: "chats",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "harness_state_files",
            schema: "chats",
            table: "chats",
            type: "character varying(366000)",
            maxLength: 366000,
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "loading_session",
            schema: "chats",
            table: "chats",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<string>(
            name: "resumable_session_id",
            schema: "chats",
            table: "chats",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "harness_states",
            schema: "chats",
            columns: table => new
            {
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                harness = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                bytes = table.Column<long>(type: "bigint", nullable: false),
                sha256 = table.Column<byte[]>(type: "bytea", nullable: false),
                saved_from = table.Column<Guid>(type: "uuid", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_harness_states", x => new { x.person_id, x.workspace_id, x.harness });
            });

        migrationBuilder.CreateTable(
            name: "personal_instructions",
            schema: "chats",
            columns: table => new
            {
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                text = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_personal_instructions", x => x.person_id);
            });

        migrationBuilder.CreateTable(
            name: "workspace_instructions",
            schema: "chats",
            columns: table => new
            {
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                text = table.Column<string>(type: "character varying(10000)", maxLength: 10000, nullable: false),
                updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                updated_by = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_workspace_instructions", x => x.workspace_id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "harness_states",
            schema: "chats");

        migrationBuilder.DropTable(
            name: "personal_instructions",
            schema: "chats");

        migrationBuilder.DropTable(
            name: "workspace_instructions",
            schema: "chats");

        migrationBuilder.DropColumn(
            name: "checkpoint_after",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "harness_state_files",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "loading_session",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "resumable_session_id",
            schema: "chats",
            table: "chats");
    }
}
