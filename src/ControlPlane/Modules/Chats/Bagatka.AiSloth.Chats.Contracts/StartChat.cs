using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Input to <see cref="IChatsApi.StartAsync"/>. The agent runs in the harness the nook carries.
/// </summary>
/// <param name="NookId">The nook the agent will work in.</param>
/// <param name="Account">The agent account that pays for its work: the workspace's, or the actor's own.</param>
public sealed record StartChat(NookId NookId, AgentAccountId Account);
