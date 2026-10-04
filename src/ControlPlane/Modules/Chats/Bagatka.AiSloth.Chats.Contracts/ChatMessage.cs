using System;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// A message someone sent to a chat.
/// </summary>
/// <param name="Id">The message.</param>
/// <param name="ChatId">Its chat.</param>
/// <param name="SentBy">Who sent it.</param>
/// <param name="Text">What it says.</param>
/// <param name="SentAt">When it was sent.</param>
/// <param name="IsProposal">Whether it is a proposal: it never reaches the agent, and the account's owner may send it on.</param>
public sealed record ChatMessage(MessageId Id, ChatId ChatId, UserId SentBy, string Text, DateTimeOffset SentAt, bool IsProposal);
