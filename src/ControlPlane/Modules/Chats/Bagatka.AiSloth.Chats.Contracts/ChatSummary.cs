using System;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// A chat as the people with access to its nook see it.
/// </summary>
/// <param name="Id">The chat.</param>
/// <param name="NookId">The nook its agent works in.</param>
/// <param name="WorkspaceId">The nook's workspace.</param>
/// <param name="StartedBy">Who started it.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="Working">Whether the agent is working on a turn, or messages wait for it.</param>
/// <param name="Harness">The harness that runs its agent.</param>
/// <param name="Model">
/// The model its agent runs on from its next turn; <see langword="null"/> only for a chat started before
/// every chat had one, which takes one (<see cref="IChatsApi.ChangeModelAsync"/>) before its next message.
/// </param>
/// <param name="Thinking">How hard its agent thinks from its next turn.</param>
/// <param name="Account">The agent account that pays for its work from its next turn.</param>
/// <param name="AccountOwner">
/// The owner of the personal account it runs on, whose messages alone reach the agent: everyone
/// else's are proposals. <see langword="null"/> on the workspace's account, which people with Write
/// on the workspace use.
/// </param>
/// <param name="Title">
/// What the chat is about, as lists show it: the first line of its first message, at most 80
/// characters; <see langword="null"/> until someone writes in it.
/// </param>
public sealed record ChatSummary(
    ChatId Id,
    NookId NookId,
    WorkspaceId WorkspaceId,
    UserId StartedBy,
    DateTimeOffset StartedAt,
    bool Working,
    string Harness,
    string? Model,
    string Thinking,
    AgentAccountId Account,
    UserId? AccountOwner,
    string? Title);
