using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.AgentAccounts.Data.Migrations;

/// <inheritdoc />
public partial class _20261011171518_ReportedLimits : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "limits_reported_at",
            schema: "agent_accounts",
            table: "accounts",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "reported_limits",
            schema: "agent_accounts",
            table: "accounts",
            type: "text",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "limits_reported_at",
            schema: "agent_accounts",
            table: "accounts");

        migrationBuilder.DropColumn(
            name: "reported_limits",
            schema: "agent_accounts",
            table: "accounts");
    }
}
