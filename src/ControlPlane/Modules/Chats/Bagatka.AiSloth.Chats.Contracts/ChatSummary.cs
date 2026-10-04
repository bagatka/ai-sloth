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
/// <param name="Account">The agent account that pays for its work.</param>
/// <param name="AccountOwner">
/// The owner of the personal account it runs on, whose messages alone reach the agent: everyone
/// else's are proposals. <see langword="null"/> on the workspace's account, which people with Write
/// on the workspace use.
/// </param>
public sealed record ChatSummary(
    ChatId Id,
    NookId NookId,
    WorkspaceId WorkspaceId,
    UserId StartedBy,
    DateTimeOffset StartedAt,
    bool Working,
    string Harness,
    AgentAccountId Account,
    UserId? AccountOwner);
