using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Machines.Data.Migrations;

/// <inheritdoc />
public partial class _20261008091523_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "machines");

        migrationBuilder.CreateTable(
            name: "machines",
            schema: "machines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                code_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                code_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_machines", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "outbox_messages",
            schema: "machines",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                type = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                payload = table.Column<string>(type: "jsonb", nullable: false),
                attempts = table.Column<int>(type: "integer", nullable: false),
                retry_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                parked_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_outbox_messages", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "placements",
            schema: "machines",
            columns: table => new
            {
                key = table.Column<Guid>(type: "uuid", nullable: false),
                machine_id = table.Column<Guid>(type: "uuid", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_placements", x => x.key);
            });

        migrationBuilder.CreateIndex(
            name: "ix_machines_code_hash",
            schema: "machines",
            table: "machines",
            column: "code_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_machines_workspace_id_id",
            schema: "machines",
            table: "machines",
            columns: new[] { "workspace_id", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_outbox_messages_retry_at",
            schema: "machines",
            table: "outbox_messages",
            column: "retry_at",
            filter: "parked_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "ix_placements_machine_id",
            schema: "machines",
            table: "placements",
            column: "machine_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "machines",
            schema: "machines");

        migrationBuilder.DropTable(
            name: "outbox_messages",
            schema: "machines");

        migrationBuilder.DropTable(
            name: "placements",
            schema: "machines");
    }
}
