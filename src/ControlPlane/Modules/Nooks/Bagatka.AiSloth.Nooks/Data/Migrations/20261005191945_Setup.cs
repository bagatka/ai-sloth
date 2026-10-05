using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Nooks.Data.Migrations;

/// <inheritdoc />
public partial class _20261005191945_Setup : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "exited_at",
            schema: "nooks",
            table: "processes",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "setup_process_id",
            schema: "nooks",
            table: "nooks",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<List<string>>(
            name: "setup_scripts",
            schema: "nooks",
            table: "nooks",
            type: "text[]",
            nullable: false,
            defaultValueSql: "'{}'");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "exited_at",
            schema: "nooks",
            table: "processes");

        migrationBuilder.DropColumn(
            name: "setup_process_id",
            schema: "nooks",
            table: "nooks");

        migrationBuilder.DropColumn(
            name: "setup_scripts",
            schema: "nooks",
            table: "nooks");
    }
}
