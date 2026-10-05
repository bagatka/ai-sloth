using System.Collections.Generic;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Input to <see cref="IChatsApi.StartAsync"/>: the chat and the nook it creates for its agent.
/// </summary>
/// <param name="WorkspaceId">The workspace that will own the chat and its nook.</param>
/// <param name="Provider">Where the nook runs, such as <c>docker</c> (<c>INooksApi.ListProvidersAsync</c>).</param>
/// <param name="Harness">The harness that runs the agent, one of <see cref="IChatsApi.ListHarnessesAsync"/>, such as <c>codex</c>.</param>
/// <param name="Account">The agent account that pays for its work: the workspace's, or the actor's own.</param>
/// <param name="Repositories">The workspace's repositories its nook starts with, each at <c>/work/&lt;name&gt;</c>; empty for none.</param>
/// <param name="CopyOf">
/// Another chat of the workspace whose nook's files its nook starts with a copy of, as they are now;
/// or <see langword="null"/>. Not together with repositories.
/// </param>
public sealed record StartChat(WorkspaceId WorkspaceId, string Provider, string Harness, AgentAccountId Account, IReadOnlyList<NookRepository> Repositories, ChatId? CopyOf);
