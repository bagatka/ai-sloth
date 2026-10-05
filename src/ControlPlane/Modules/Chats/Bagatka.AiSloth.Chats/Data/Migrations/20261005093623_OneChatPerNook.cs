using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261005093623_OneChatPerNook : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_chats_nook_id_id",
            schema: "chats",
            table: "chats");

        migrationBuilder.CreateIndex(
            name: "ix_chats_nook_id",
            schema: "chats",
            table: "chats",
            column: "nook_id",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_chats_workspace_id_id",
            schema: "chats",
            table: "chats",
            columns: new[] { "workspace_id", "id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_chats_nook_id",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropIndex(
            name: "ix_chats_workspace_id_id",
            schema: "chats",
            table: "chats");

        migrationBuilder.CreateIndex(
            name: "ix_chats_nook_id_id",
            schema: "chats",
            table: "chats",
            columns: new[] { "nook_id", "id" });
    }
}
