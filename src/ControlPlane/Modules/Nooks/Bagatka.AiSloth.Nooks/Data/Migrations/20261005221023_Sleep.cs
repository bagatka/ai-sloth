using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Nooks.Data.Migrations;

/// <inheritdoc />
public partial class _20261005221023_Sleep : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<bool>(
            name: "resume_due",
            schema: "nooks",
            table: "nooks",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "slept_at",
            schema: "nooks",
            table: "nooks",
            type: "timestamp with time zone",
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "resume_due",
            schema: "nooks",
            table: "nooks");

        migrationBuilder.DropColumn(
            name: "slept_at",
            schema: "nooks",
            table: "nooks");
    }
}
