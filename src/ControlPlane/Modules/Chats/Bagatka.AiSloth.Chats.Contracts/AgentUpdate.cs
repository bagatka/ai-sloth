using System.Text.Json;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Progress the agent reported during a turn: text, thoughts, tool calls, plans, and more, as the
/// <c>update</c> of an Agent Client Protocol <c>session/update</c> notification, unchanged. Its
/// <c>sessionUpdate</c> field says which kind it is (https://agentclientprotocol.com/protocol/prompt-turn).
/// </summary>
/// <param name="Update">The update.</param>
public sealed record AgentUpdate(JsonElement Update);
