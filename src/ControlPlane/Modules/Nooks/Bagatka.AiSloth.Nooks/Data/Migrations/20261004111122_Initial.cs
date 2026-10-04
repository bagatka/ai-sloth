using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Nooks.Data.Migrations;

/// <inheritdoc />
public partial class _20261004111122_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "nooks");

        migrationBuilder.CreateTable(
            name: "nooks",
            schema: "nooks",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                provider = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                daemon_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                disk_total_bytes = table.Column<long>(type: "bigint", nullable: true),
                disk_available_bytes = table.Column<long>(type: "bigint", nullable: true),
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
                exit_code = table.Column<int>(type: "integer", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_processes", x => x.id);
            });

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
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "nooks",
            schema: "nooks");

        migrationBuilder.DropTable(
            name: "processes",
            schema: "nooks");
    }
}
