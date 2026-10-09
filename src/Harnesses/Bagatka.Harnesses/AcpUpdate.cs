using System.Text.Json;

namespace Bagatka.Harnesses;

/// <summary>
/// Progress the agent reported: the <c>update</c> of a <c>session/update</c> notification, unchanged.
/// Its <c>sessionUpdate</c> field says which kind it is, such as <c>agent_message_chunk</c> or <c>tool_call</c>.
/// </summary>
/// <param name="Update">The update.</param>
public sealed record AcpUpdate(JsonElement Update)
{
    /// <summary>
    /// The model that answered, from a <c>usage_update</c>, when the agent says, as Claude's adapter
    /// does in its <c>_meta</c>, such as <c>claude-sonnet-5</c>; otherwise null.
    /// </summary>
    public string? Model
    {
        get
        {
            JsonElement? meta = Json.Property(Update, "_meta");
            JsonElement? model = meta is null ? null : Json.Property(meta.Value, "_claude/model");
            return model?.ValueKind == JsonValueKind.String ? model.Value.GetString() : null;
        }
    }
}
