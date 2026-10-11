using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261011083806_ChatModelsAndPresets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "model",
            schema: "chats",
            table: "chats",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true);

        migrationBuilder.CreateTable(
            name: "presets",
            schema: "chats",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                harness = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                agent_account_id = table.Column<Guid>(type: "uuid", nullable: false),
                model = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                added_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_presets", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_presets_owner_id_harness_agent_account_id_model",
            schema: "chats",
            table: "presets",
            columns: new[] { "owner_id", "harness", "agent_account_id", "model" },
            unique: true)
            .Annotation("Npgsql:NullsDistinct", false);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "presets",
            schema: "chats");

        migrationBuilder.DropColumn(
            name: "model",
            schema: "chats",
            table: "chats");
    }
}
