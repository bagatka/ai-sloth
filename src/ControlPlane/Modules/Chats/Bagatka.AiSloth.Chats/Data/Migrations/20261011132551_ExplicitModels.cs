using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Bagatka.AiSloth.Chats.Data.Migrations;

/// <inheritdoc />
public partial class _20261011132551_ExplicitModels : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Every chat, preset, and switch of model now has a thinking level; one left to its harness gets
        // the harness's default (HarnessProfile.DefaultThinking). A preset without a model is removed: no
        // model can be told for it, and so is a switch to the agent's default model, which no longer
        // exists. A chat without a model keeps none until someone chooses (ChatsErrors.ModelNotChosen).
        migrationBuilder.Sql("""
            DELETE FROM chats.presets WHERE model IS NULL;
            DELETE FROM chats.events WHERE kind = 'model-changed' AND data->>'model' IS NULL;
            UPDATE chats.events AS changed
                SET data = jsonb_set(changed.data, '{thinking}', to_jsonb(CASE chat.harness WHEN 'claude-code' THEN 'high' ELSE 'medium' END))
                FROM chats.chats AS chat
                WHERE chat.id = changed.chat_id AND changed.kind = 'model-changed' AND changed.data->>'thinking' IS NULL;
            UPDATE chats.presets SET thinking = CASE harness WHEN 'claude-code' THEN 'high' ELSE 'medium' END WHERE thinking IS NULL;
            UPDATE chats.chats SET thinking = CASE harness WHEN 'claude-code' THEN 'high' ELSE 'medium' END WHERE thinking IS NULL;
            UPDATE chats.chats SET next_thinking = CASE harness WHEN 'claude-code' THEN 'high' ELSE 'medium' END
                WHERE agent_change_waiting AND next_thinking IS NULL;
            """);

        migrationBuilder.AlterColumn<string>(
            name: "thinking",
            schema: "chats",
            table: "presets",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(16)",
            oldMaxLength: 16,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "model",
            schema: "chats",
            table: "presets",
            type: "character varying(200)",
            maxLength: 200,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200,
            oldNullable: true);

        migrationBuilder.AlterColumn<string>(
            name: "thinking",
            schema: "chats",
            table: "chats",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "",
            oldClrType: typeof(string),
            oldType: "character varying(16)",
            oldMaxLength: 16,
            oldNullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<string>(
            name: "thinking",
            schema: "chats",
            table: "presets",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(16)",
            oldMaxLength: 16);

        migrationBuilder.AlterColumn<string>(
            name: "model",
            schema: "chats",
            table: "presets",
            type: "character varying(200)",
            maxLength: 200,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(200)",
            oldMaxLength: 200);

        migrationBuilder.AlterColumn<string>(
            name: "thinking",
            schema: "chats",
            table: "chats",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(16)",
            oldMaxLength: 16);
    }
}
