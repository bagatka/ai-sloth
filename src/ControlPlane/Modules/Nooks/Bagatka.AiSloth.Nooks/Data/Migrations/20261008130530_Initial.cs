using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Nooks.Data.Migrations;

/// <inheritdoc />
public partial class _20261008130530_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "nooks");

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
            name: "kept_folders",
            schema: "nooks",
            columns: table => new
            {
                name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                head = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                saves = table.Column<List<string>>(type: "text[]", nullable: false),
                bytes = table.Column<long>(type: "bigint", nullable: false),
                saved_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                saved_by = table.Column<Guid>(type: "uuid", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_kept_folders", x => x.name);
            });

        migrationBuilder.CreateTable(
            name: "nooks",
            schema: "nooks",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                location = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                image = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: true),
                reserved_for = table.Column<Guid>(type: "uuid", nullable: true),
                copy_of = table.Column<Guid>(type: "uuid", nullable: true),
                copy_checkpoint = table.Column<int>(type: "integer", nullable: true),
                kept_paths = table.Column<List<string>>(type: "text[]", nullable: false),
                from_scratch = table.Column<bool>(type: "boolean", nullable: false),
                ready_copy_made_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ready_copy_due = table.Column<bool>(type: "boolean", nullable: false),
                sources_ready = table.Column<bool>(type: "boolean", nullable: false),
                setup_scripts = table.Column<List<string>>(type: "text[]", nullable: false),
                setup_process_id = table.Column<Guid>(type: "uuid", nullable: true),
                slept_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                evicted = table.Column<bool>(type: "boolean", nullable: false),
                resume_due = table.Column<bool>(type: "boolean", nullable: false),
                daemon_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_nooks", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "processes",
            schema: "nooks",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                nook_id = table.Column<Guid>(type: "uuid", nullable: false),
                command = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                arguments = table.Column<List<string>>(type: "text[]", nullable: false),
                working_directory = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                retention = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                exit_code = table.Column<int>(type: "integer", nullable: true),
                exited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_processes", x => x.id);
            });

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

        migrationBuilder.CreateIndex(
            name: "ix_nooks_status_id",
            schema: "nooks",
            table: "nooks",
            columns: new[] { "status", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_nooks_workspace_id_id",
            schema: "nooks",
            table: "nooks",
            columns: new[] { "workspace_id", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_processes_nook_id_id",
            schema: "nooks",
            table: "processes",
            columns: new[] { "nook_id", "id" });

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
            name: "checkpoint_parts",
            schema: "nooks");

        migrationBuilder.DropTable(
            name: "kept_folders",
            schema: "nooks");

        migrationBuilder.DropTable(
            name: "nooks",
            schema: "nooks");

        migrationBuilder.DropTable(
            name: "processes",
            schema: "nooks");

        migrationBuilder.DropTable(
            name: "ready_copies",
            schema: "nooks");

        migrationBuilder.DropTable(
            name: "source_copies",
            schema: "nooks");

        migrationBuilder.DropTable(
            name: "checkpoints",
            schema: "nooks");
    }
}
