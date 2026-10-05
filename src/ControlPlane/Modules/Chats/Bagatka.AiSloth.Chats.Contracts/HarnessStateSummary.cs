using System;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>A person's state for one harness, as <see cref="IChatsApi.ListHarnessStatesAsync"/> shows it.</summary>
/// <param name="Harness">The harness, such as <c>claude-code</c>.</param>
/// <param name="SavedAt">When a chat last saved it.</param>
/// <param name="Bytes">The size of its archive.</param>
/// <param name="SavedFrom">The chat that saved it.</param>
public sealed record HarnessStateSummary(string Harness, DateTimeOffset SavedAt, long Bytes, ChatId SavedFrom);
