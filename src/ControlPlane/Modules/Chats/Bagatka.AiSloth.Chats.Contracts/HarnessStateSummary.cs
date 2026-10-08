using System;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>Harness state for one harness, as the list of a workspace's harness state shows it.</summary>
/// <param name="Harness">The harness, such as <c>claude-code</c>.</param>
/// <param name="Shared">
/// Whether it is the workspace's, kept from the chats on the workspace's accounts and given to all of
/// them, rather than the actor's own, kept from the chats on their own accounts.
/// </param>
/// <param name="SavedAt">When a chat last saved it.</param>
/// <param name="Bytes">The size of its history.</param>
/// <param name="SavedFrom">The chat that saved it last, unless that chat is gone.</param>
public sealed record HarnessStateSummary(string Harness, bool Shared, DateTimeOffset SavedAt, long Bytes, ChatId? SavedFrom);
