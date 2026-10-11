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
/// <param name="Model">
/// The model its agent runs on, as the account's vendor names it, such as <c>openai/gpt-6-luna</c>
/// (<see cref="IChatsApi.ListModelsAsync"/>, whose first a new chat starts on unless someone chose).
/// </param>
/// <param name="Thinking">
/// How hard its agent thinks, one of its harness's <see cref="HarnessSummary.ThinkingLevels"/>, such as
/// its <see cref="HarnessSummary.DefaultThinking"/>.
/// </param>
/// <param name="Repositories">The workspace's repositories its nook starts with, each at <c>/work/&lt;name&gt;</c>; empty for none.</param>
/// <param name="CopyOf">
/// Another chat of the workspace whose nook's files its nook starts with, with its repositories; or
/// <see langword="null"/>. Not together with repositories. Its conversation comes only with
/// <paramref name="WithConversation"/>.
/// </param>
/// <param name="Checkpoint">
/// With <paramref name="CopyOf"/>, which of its checkpoints (<see cref="CheckpointSaved"/>) to start
/// from; <see langword="null"/> for its files as they are when the new agent starts.
/// </param>
/// <param name="WithConversation">
/// With <paramref name="CopyOf"/>, whether the new chat continues that chat's conversation: up to the
/// checkpoint, or all of it now, as a Markdown file in its nook (<c>/work/.aisloth/chats/&lt;id&gt;.md</c>)
/// that its agent's instructions name. The agent starts a session of its own, and reads the file
/// when it needs it.
/// </param>
public sealed record StartChat(
    WorkspaceId WorkspaceId,
    string Provider,
    string Harness,
    AgentAccountId Account,
    string Model,
    string Thinking,
    IReadOnlyList<NookRepository> Repositories,
    ChatId? CopyOf,
    int? Checkpoint,
    bool WithConversation);
