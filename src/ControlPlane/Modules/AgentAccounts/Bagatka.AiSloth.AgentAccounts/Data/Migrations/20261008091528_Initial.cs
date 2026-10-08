using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.AgentAccounts.Data.Migrations;

/// <inheritdoc />
public partial class _20261008091528_Initial : Migration
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
                endpoint = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                sign_in_ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                sealed_secret = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_accounts", x => x.id);
                table.CheckConstraint("ck_accounts_one_owner", "(workspace_id IS NULL) <> (owner_id IS NULL)");
            });

        migrationBuilder.CreateTable(
            name: "sign_ins",
            schema: "agent_accounts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                callback = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                state = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                nonce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                sealed_verifier = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sign_ins", x => x.id);
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

        migrationBuilder.CreateIndex(
            name: "ix_sign_ins_user_id_expires_at",
            schema: "agent_accounts",
            table: "sign_ins",
            columns: new[] { "user_id", "expires_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "accounts",
            schema: "agent_accounts");

        migrationBuilder.DropTable(
            name: "sign_ins",
            schema: "agent_accounts");
    }
}
