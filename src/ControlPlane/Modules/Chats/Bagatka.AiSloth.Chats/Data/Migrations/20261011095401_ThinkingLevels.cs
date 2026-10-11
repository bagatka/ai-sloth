using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261011095401_ThinkingLevels : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_presets_owner_id_harness_agent_account_id_model",
            schema: "chats",
            table: "presets");

        migrationBuilder.AddColumn<string>(
            name: "thinking",
            schema: "chats",
            table: "presets",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "next_thinking",
            schema: "chats",
            table: "chats",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "thinking",
            schema: "chats",
            table: "chats",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.CreateIndex(
            name: "ix_presets_owner_id_harness_agent_account_id_model_thinking",
            schema: "chats",
            table: "presets",
            columns: new[] { "owner_id", "harness", "agent_account_id", "model", "thinking" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "ix_presets_owner_id_harness_agent_account_id_model_thinking",
            schema: "chats",
            table: "presets");

        migrationBuilder.DropColumn(
            name: "thinking",
            schema: "chats",
            table: "presets");

        migrationBuilder.DropColumn(
            name: "next_thinking",
            schema: "chats",
            table: "chats");

        migrationBuilder.DropColumn(
            name: "thinking",
            schema: "chats",
            table: "chats");

        migrationBuilder.CreateIndex(
            name: "ix_presets_owner_id_harness_agent_account_id_model",
            schema: "chats",
            table: "presets",
            columns: new[] { "owner_id", "harness", "agent_account_id", "model" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);
    }
}
