using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Input to <see cref="IChatsApi.StartAsync"/>: the chat and the nook it creates for its agent.
/// </summary>
/// <param name="WorkspaceId">The workspace that will own the chat and its nook.</param>
/// <param name="Provider">Where the nook runs, such as <c>docker</c> (<c>INooksApi.ListProvidersAsync</c>).</param>
/// <param name="Harness">The harness that runs the agent, one of <see cref="IChatsApi.ListHarnessesAsync"/>, such as <c>codex</c>.</param>
/// <param name="Account">The agent account that pays for its work: the workspace's, or the actor's own.</param>
public sealed record StartChat(WorkspaceId WorkspaceId, string Provider, string Harness, AgentAccountId Account);
