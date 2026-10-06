using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.Foundation.Modules.Data.Migrations;

/// <inheritdoc />
public partial class _20261006172455_InstanceLease : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(
            name: "hosting");

        migrationBuilder.CreateTable(
            name: "instance_lease",
            schema: "hosting",
            columns: table => new
            {
                id = table.Column<int>(type: "integer", nullable: false),
                holder = table.Column<Guid>(type: "uuid", nullable: true),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_instance_lease", x => x.id);
            });

        migrationBuilder.InsertData(
            schema: "hosting",
            table: "instance_lease",
            columns: new[] { "id", "expires_at", "holder" },
            values: new object[] { 1, new DateTimeOffset(new DateTime(2000, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)), null });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "instance_lease",
            schema: "hosting");
    }
}
