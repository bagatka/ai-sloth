using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>Someone sent a message; it waits for the agent until a turn starts with it or it is steered into one.</summary>
/// <param name="MessageId">The message.</param>
/// <param name="SentBy">Who sent it.</param>
/// <param name="Text">What it says.</param>
/// <param name="Proposal">The proposal it sends on, if any.</param>
public sealed record MessageSent(MessageId MessageId, UserId SentBy, string Text, MessageId? Proposal);
