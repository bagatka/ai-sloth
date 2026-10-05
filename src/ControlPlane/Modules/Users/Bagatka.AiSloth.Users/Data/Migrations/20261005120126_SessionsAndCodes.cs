using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Users.Data.Migrations;

/// <inheritdoc />
public partial class _20261005120126_SessionsAndCodes : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "subject",
            schema: "users",
            table: "users",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(255)",
            oldMaxLength: 255);

        migrationBuilder.AlterColumn<string>(
            name: "issuer",
            schema: "users",
            table: "users",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(2048)",
            oldMaxLength: 2048);

        migrationBuilder.AddColumn<string>(
            name: "name",
            schema: "users",
            table: "users",
            type: "character varying(100)",
            maxLength: 100,
            nullable: false,
            defaultValue: "");

        migrationBuilder.CreateTable(
            name: "issued_codes",
            schema: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                purpose = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: true),
                code_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_issued_codes", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "sessions",
            schema: "users",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                user_id = table.Column<Guid>(type: "uuid", nullable: false),
                device = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                token_hash = table.Column<byte[]>(type: "bytea", nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                last_used_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("pk_sessions", x => x.id);
            });

        migrationBuilder.AddCheckConstraint(
            name: "ck_users_identity",
            schema: "users",
            table: "users",
            sql: "(issuer IS NULL) = (subject IS NULL)");

        migrationBuilder.CreateIndex(
            name: "ix_issued_codes_code_hash",
            schema: "users",
            table: "issued_codes",
            column: "code_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_issued_codes_user_id_expires_at",
            schema: "users",
            table: "issued_codes",
            columns: new[] { "user_id", "expires_at" });

        migrationBuilder.CreateIndex(
            name: "ix_sessions_token_hash",
            schema: "users",
            table: "sessions",
            column: "token_hash",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_sessions_user_id",
            schema: "users",
            table: "sessions",
            column: "user_id");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "issued_codes",
            schema: "users");

        migrationBuilder.DropTable(
            name: "sessions",
            schema: "users");

        migrationBuilder.DropCheckConstraint(
            name: "ck_users_identity",
            schema: "users",
            table: "users");

        migrationBuilder.DropColumn(
            name: "name",
            schema: "users",
            table: "users");

        migrationBuilder.AlterColumn<string>(
            name: "subject",
            schema: "users",
            table: "users",
            type: "character varying(255)",
            maxLength: 255,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(255)",
            oldMaxLength: 255,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "issuer",
            schema: "users",
            table: "users",
            type: "character varying(2048)",
            maxLength: 2048,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(2048)",
            oldMaxLength: 2048,
            oldNullable: true);
    }
}
