using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Workspaces.Data.Migrations;

/// <inheritdoc />
public partial class _20261008091522_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "workspaces");

        migrationBuilder.CreateTable(
            name: "grants",
            schema: "workspaces",
            columns: table => new
            {
                resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                resource_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                access = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_grants", x => new { x.resource_id, x.user_id });
            });

        migrationBuilder.CreateTable(
            name: "invites",
            schema: "workspaces",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                resource_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                resource_id = table.Column<Guid>(type: "uuid", nullable: false),
                access = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                created_by = table.Column<Guid>(type: "uuid", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                accepted_by = table.Column<Guid>(type: "uuid", nullable: true),
                accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_invites", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "links",
            schema: "workspaces",
            columns: table => new
            {
                child_id = table.Column<Guid>(type: "uuid", nullable: false),
                parent_id = table.Column<Guid>(type: "uuid", nullable: false),
                child_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                parent_kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_links", x => new { x.child_id, x.parent_id });
            });

        migrationBuilder.CreateTable(
            name: "workspaces",
            schema: "workspaces",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_workspaces", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_grants_user_id_resource_id",
            schema: "workspaces",
            table: "grants",
            columns: new[] { "user_id", "resource_id" });

        migrationBuilder.CreateIndex(
            name: "ix_invites_code_hash",
            schema: "workspaces",
            table: "invites",
            column: "code_hash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "grants",
            schema: "workspaces");

        migrationBuilder.DropTable(
            name: "invites",
            schema: "workspaces");

        migrationBuilder.DropTable(
            name: "links",
            schema: "workspaces");

        migrationBuilder.DropTable(
            name: "workspaces",
            schema: "workspaces");
    }
}
