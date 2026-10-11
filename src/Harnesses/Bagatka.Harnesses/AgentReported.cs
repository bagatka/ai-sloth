using System.Text.Json;

namespace Bagatka.Harnesses;

/// <summary>
/// Progress the agent reported: the <c>update</c> of ACP's <c>session/update</c>, unchanged. Its
/// <c>sessionUpdate</c> field says which kind it is, such as <c>agent_message_chunk</c> or <c>tool_call</c>.
/// </summary>
/// <param name="Update">The update.</param>
/// <param name="Model">The model that answered, when the agent says, as Claude's adapter does, such as <c>claude-sonnet-5</c>.</param>
public sealed record AgentReported(JsonElement Update, string? Model);
