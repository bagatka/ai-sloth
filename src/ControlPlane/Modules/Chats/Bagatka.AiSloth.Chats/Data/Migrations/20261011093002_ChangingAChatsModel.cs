using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261011093002_ChangingAChatsModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "agent_change_waiting",
            schema: "chats",
            table: "chats",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<Guid>(
            name: "next_agent_account_id",
            schema: "chats",
            table: "chats",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "next_model",
            schema: "chats",
            table: "chats",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "agent_change_waiting",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "next_agent_account_id",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "next_model",
            schema: "chats",
            table: "chats");
    }
}
