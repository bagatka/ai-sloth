using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261011172846_ContinuedChats : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<Guid>(
            name: "continues_from",
            schema: "chats",
            table: "chats",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "continues_up_to",
            schema: "chats",
            table: "chats",
            type: "bigint",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "continues_from",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "continues_up_to",
            schema: "chats",
            table: "chats");
    }
}
