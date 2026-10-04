using System;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// A chat as the members of its workspace see it.
/// </summary>
/// <param name="Id">The chat.</param>
/// <param name="NookId">The nook its agent works in.</param>
/// <param name="WorkspaceId">The nook's workspace.</param>
/// <param name="StartedBy">Who started it.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="Working">Whether the agent is working on a turn, or messages wait for it.</param>
public sealed record ChatSummary(ChatId Id, NookId NookId, WorkspaceId WorkspaceId, UserId StartedBy, DateTimeOffset StartedAt, bool Working);
