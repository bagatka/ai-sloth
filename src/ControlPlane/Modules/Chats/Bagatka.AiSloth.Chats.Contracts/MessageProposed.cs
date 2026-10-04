using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Someone whose messages don't reach the agent proposed one: the chat runs on another person's
/// account. Its owner may send it on, as is or edited (<see cref="SendMessage.Proposal"/>).
/// </summary>
/// <param name="MessageId">The proposal.</param>
/// <param name="ProposedBy">Who proposed it.</param>
/// <param name="Text">What it says.</param>
public sealed record MessageProposed(MessageId MessageId, UserId ProposedBy, string Text);
