using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261004152155_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "chats");

        migrationBuilder.CreateTable(
            name: "chats",
            schema: "chats",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                nook_id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                started_by = table.Column<Guid>(type: "uuid", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                harness_process_id = table.Column<Guid>(type: "uuid", nullable: true),
                harness_token_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                output_offset = table.Column<long>(type: "bigint", nullable: false),
                session_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                supports_steering = table.Column<bool>(type: "boolean", nullable: false),
                turn_message_id = table.Column<Guid>(type: "uuid", nullable: true),
                last_sequence = table.Column<long>(type: "bigint", nullable: false),
                xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_chats", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "events",
            schema: "chats",
            columns: table => new
            {
                chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                sequence = table.Column<long>(type: "bigint", nullable: false),
                at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                data = table.Column<string>(type: "jsonb", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_events", x => new { x.chat_id, x.sequence });
            });

        migrationBuilder.CreateTable(
            name: "messages",
            schema: "chats",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                chat_id = table.Column<Guid>(type: "uuid", nullable: false),
                sent_by = table.Column<Guid>(type: "uuid", nullable: false),
                text = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                sent_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                state = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_messages", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_chats_harness_token_hash",
            schema: "chats",
            table: "chats",
            column: "harness_token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_chats_nook_id_id",
            schema: "chats",
            table: "chats",
            columns: new[] { "nook_id", "id" });

        migrationBuilder.CreateIndex(
            name: "ix_messages_chat_id_state_id",
            schema: "chats",
            table: "messages",
            columns: new[] { "chat_id", "state", "id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "chats",
            schema: "chats");

        migrationBuilder.DropTable(
            name: "events",
            schema: "chats");

        migrationBuilder.DropTable(
            name: "messages",
            schema: "chats");
    }
}
