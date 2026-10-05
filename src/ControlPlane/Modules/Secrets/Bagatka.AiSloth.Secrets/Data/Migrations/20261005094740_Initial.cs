using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Secrets.Data.Migrations;

/// <inheritdoc />
public partial class _20261005094740_Initial : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "secrets");

        migrationBuilder.CreateTable(
            name: "secrets",
            schema: "secrets",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                workspace_id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                set_by = table.Column<Guid>(type: "uuid", nullable: false),
                set_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                sealed_value = table.Column<byte[]>(type: "bytea", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_secrets", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_secrets_workspace_id_name",
            schema: "secrets",
            table: "secrets",
            columns: new[] { "workspace_id", "name" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "secrets",
            schema: "secrets");
    }
}
