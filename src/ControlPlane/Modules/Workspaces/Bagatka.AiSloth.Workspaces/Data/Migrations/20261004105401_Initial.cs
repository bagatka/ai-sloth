using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Workspaces.Data.Migrations;

/// <inheritdoc />
public partial class _20261004105401_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "workspaces");

        migrationBuilder.CreateTable(
            name: "workspaces",
            schema: "workspaces",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_workspaces", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "members",
            schema: "workspaces",
            columns: table => new
            {
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                role = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_members", x => new { x.workspace_id, x.user_id });
                table.ForeignKey(
                    name: "fk_members_workspaces_workspace_id",
                    column: x => x.workspace_id,
                    principalSchema: "workspaces",
                    principalTable: "workspaces",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "ix_members_user_id_workspace_id",
            schema: "workspaces",
            table: "members",
            columns: new[] { "user_id", "workspace_id" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "members",
            schema: "workspaces");

        migrationBuilder.DropTable(
            name: "workspaces",
            schema: "workspaces");
    }
}
