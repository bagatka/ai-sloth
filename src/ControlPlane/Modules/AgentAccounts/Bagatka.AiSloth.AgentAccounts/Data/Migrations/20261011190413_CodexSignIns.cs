using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.AgentAccounts.Data.Migrations;

/// <inheritdoc />
public partial class _20261011190413_CodexSignIns : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "user_code",
            schema: "agent_accounts",
            table: "code_sign_ins",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "user_code",
            schema: "agent_accounts",
            table: "code_sign_ins");
    }
}
