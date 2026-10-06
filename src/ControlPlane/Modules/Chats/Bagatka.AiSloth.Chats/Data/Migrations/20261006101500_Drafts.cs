using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261006101500_Drafts : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "drafts",
            schema: "chats",
            columns: table => new
            {
                chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                nook_id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                started_by = table.Column<Guid>(type: "uuid", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_drafts", x => x.chat_id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_drafts_started_at",
            schema: "chats",
            table: "drafts",
            column: "started_at");

        migrationBuilder.CreateIndex(
            name: "ix_drafts_started_by_workspace_id_started_at",
            schema: "chats",
            table: "drafts",
            columns: new[] { "started_by", "workspace_id", "started_at" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "drafts",
            schema: "chats");
    }
}
