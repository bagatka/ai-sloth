using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.AgentAccounts.Data.Migrations;

/// <inheritdoc />
public partial class _20261011194950_OneWayPerPlan : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // One way to each plan: a ChatGPT plan signs in as Codex, a Claude plan as Claude Code. Plans
        // signed in with ChatGPT and Claude's setup tokens go, and their owners add them again; Codex
        // plans are ChatGPT plans now. Sign-ins in progress start again.
        migrationBuilder.Sql("""
            DELETE FROM agent_accounts.accounts WHERE kind IN ('ChatGptPlan', 'ClaudePlan');
            UPDATE agent_accounts.accounts SET kind = 'ChatGptPlan' WHERE kind = 'CodexPlan';
            DELETE FROM agent_accounts.sign_ins;
            DELETE FROM agent_accounts.code_sign_ins WHERE kind = 'CodexPlan';
            """);

        migrationBuilder.DropColumn(
            name: "callback",
            schema: "agent_accounts",
            table: "sign_ins");

        migrationBuilder.DropColumn(
            name: "nonce",
            schema: "agent_accounts",
            table: "sign_ins");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "callback",
            schema: "agent_accounts",
            table: "sign_ins",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: false,
            defaultValue: "");

        migrationBuilder.AddColumn<string>(
            name: "nonce",
            schema: "agent_accounts",
            table: "sign_ins",
            type: "character varying(64)",
            maxLength: 64,
            nullable: false,
            defaultValue: "");
    }
}
