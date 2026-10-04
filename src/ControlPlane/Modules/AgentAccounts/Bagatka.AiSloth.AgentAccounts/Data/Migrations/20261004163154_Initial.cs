using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.AgentAccounts.Data.Migrations;

/// <inheritdoc />
public partial class _20261004163154_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "agent_accounts");

        migrationBuilder.CreateTable(
            name: "accounts",
            schema: "agent_accounts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: true),
                owner_id = table.Column<Guid>(type: "uuid", nullable: true),
                added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                sealed_secret = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_accounts", x => x.id);
                table.CheckConstraint("ck_accounts_one_owner", "(workspace_id IS NULL) <> (owner_id IS NULL)");
            });

        migrationBuilder.CreateIndex(
            name: "ix_accounts_owner_id_id",
            schema: "agent_accounts",
            table: "accounts",
            columns: new[] { "owner_id", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_accounts_workspace_id_id",
            schema: "agent_accounts",
            table: "accounts",
            columns: new[] { "workspace_id", "id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "accounts",
            schema: "agent_accounts");
    }
}
