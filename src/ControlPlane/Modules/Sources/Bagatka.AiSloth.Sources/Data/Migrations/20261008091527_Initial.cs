using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Sources.Data.Migrations;

/// <inheritdoc />
public partial class _20261008091527_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "sources");

        migrationBuilder.CreateTable(
            name: "git_settings",
            schema: "sources",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                author_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                author_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                committer_name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                committer_email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                ai_sloth_co_author = table.Column<bool>(type: "boolean", nullable: false),
                branch_prefix = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_git_settings", x => x.user_id);
            });

        migrationBuilder.CreateTable(
            name: "github_connection_attempts",
            schema: "sources",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                sealed_device_code = table.Column<byte[]>(type: "bytea", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                next_poll_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_github_connection_attempts", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "github_connections",
            schema: "sources",
            columns: table => new
            {
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                git_hub_user_id = table.Column<long>(type: "bigint", nullable: false),
                login = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                connected_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                sealed_access_token = table.Column<byte[]>(type: "bytea", nullable: false),
                access_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                sealed_refresh_token = table.Column<byte[]>(type: "bytea", nullable: true),
                refresh_expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_github_connections", x => x.user_id);
            });

        migrationBuilder.CreateTable(
            name: "repositories",
            schema: "sources",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                owner = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                default_branch = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                url = table.Column<string>(type: "text", nullable: false),
                added_by = table.Column<Guid>(type: "uuid", nullable: false),
                added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_repositories", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_github_connection_attempts_user_id_expires_at",
            schema: "sources",
            table: "github_connection_attempts",
            columns: new[] { "user_id", "expires_at" });

        migrationBuilder.CreateIndex(
            name: "ix_repositories_workspace_id_name",
            schema: "sources",
            table: "repositories",
            columns: new[] { "workspace_id", "name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "git_settings",
            schema: "sources");

        migrationBuilder.DropTable(
            name: "github_connection_attempts",
            schema: "sources");

        migrationBuilder.DropTable(
            name: "github_connections",
            schema: "sources");

        migrationBuilder.DropTable(
            name: "repositories",
            schema: "sources");
    }
}
