using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261005194218_Setup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "setup_test",
            schema: "chats",
            table: "messages",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "setup_exit_code",
            schema: "chats",
            table: "chats",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "setup_run_id",
            schema: "chats",
            table: "chats",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "setup_test_after",
            schema: "chats",
            table: "chats",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "starts_agent",
            schema: "chats",
            table: "chats",
            type: "boolean",
            nullable: false,
            defaultValue: false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "setup_test",
            schema: "chats",
            table: "messages");

        migrationBuilder.DropColumn(
            name: "setup_exit_code",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "setup_run_id",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "setup_test_after",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "starts_agent",
            schema: "chats",
            table: "chats");
    }
}
