using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.AgentAccounts.Data.Migrations;

/// <inheritdoc />
public partial class _20261011142808_CodeSignIns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "code_sign_ins",
            schema: "agent_accounts",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                sealed_device_code = table.Column<byte[]>(type: "bytea", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                interval = table.Column<TimeSpan>(type: "interval", nullable: false),
                next_poll_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_code_sign_ins", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_code_sign_ins_user_id_expires_at",
            schema: "agent_accounts",
            table: "code_sign_ins",
            columns: new[] { "user_id", "expires_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "code_sign_ins",
            schema: "agent_accounts");
    }
}
