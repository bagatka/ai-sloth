using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261011165318_ModelCalls : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "model_calls",
            schema: "chats",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                nook_id = table.Column<Guid>(type: "uuid", nullable: false),
                agent_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                turn = table.Column<Guid>(type: "uuid", nullable: true),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                input = table.Column<int>(type: "integer", nullable: false),
                cache_read = table.Column<int>(type: "integer", nullable: false),
                cache_write = table.Column<int>(type: "integer", nullable: false),
                cache_write_long = table.Column<int>(type: "integer", nullable: false),
                output = table.Column<int>(type: "integer", nullable: false),
                reasoning = table.Column<int>(type: "integer", nullable: false),
                cost = table.Column<decimal>(type: "numeric(18,8)", precision: 18, scale: 8, nullable: true),
                estimate = table.Column<bool>(type: "boolean", nullable: false),
                cache_warm_until = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_model_calls", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_model_calls_chat_id_at",
            schema: "chats",
            table: "model_calls",
            columns: new[] { "chat_id", "at" });

        migrationBuilder.CreateIndex(
            name: "ix_model_calls_workspace_id_at",
            schema: "chats",
            table: "model_calls",
            columns: new[] { "workspace_id", "at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "model_calls",
            schema: "chats");
    }
}
