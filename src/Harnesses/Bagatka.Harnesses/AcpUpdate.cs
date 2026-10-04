using System.Text.Json;

namespace Bagatka.Harnesses;

/// <summary>
/// Progress the agent reported: the <c>update</c> of a <c>session/update</c> notification, unchanged.
/// Its <c>sessionUpdate</c> field says which kind it is, such as <c>agent_message_chunk</c> or <c>tool_call</c>.
/// </summary>
/// <param name="Update">The update.</param>
public sealed record AcpUpdate(JsonElement Update);
