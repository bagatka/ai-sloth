using System;
using System.Globalization;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Model;

// A message someone sent to a chat: for the agent, or a proposal from someone who may not use the
// chat's account. Sending only records it; the chat's runner announces it, and delivers the agent's.
internal sealed class Message
{
    public const int MaxTextLength = 100_000;

    // Used by Send and by EF: parameter names match property names.
    private Message(MessageId id, ChatId chatId, UserId sentBy, string text, DateTimeOffset sentAt, MessageState state, bool isProposal, MessageId? proposalId)
    {
        Id = id;
        ChatId = chatId;
        SentBy = sentBy;
        Text = text;
        SentAt = sentAt;
        State = state;
        IsProposal = isProposal;
        ProposalId = proposalId;
    }

    public MessageId Id { get; private set; }

    public ChatId ChatId { get; private set; }

    public UserId SentBy { get; private set; }

    public string Text { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    public MessageState State { get; private set; }

    // A proposal never reaches the agent.
    public bool IsProposal { get; private set; }

    // The proposal this message sends on, if any.
    public MessageId? ProposalId { get; private set; }

    // Which test of the project's setup its turn's end calls for: 1 for a request to prepare it, the
    // next one for a fix after a failed test; none for other messages.
    public int? SetupTest { get; private set; }

    public bool Waiting => State is MessageState.New or MessageState.Queued or MessageState.Steering;

    public static Result<Message> Send(ChatId chatId, UserId sentBy, string? text, bool isProposal, MessageId? proposalId, int? setupTest, TimeProvider time)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxTextLength} characters, not only spaces.");
            return new Result<Message>(Error.Validation("text", message));
        }

        return new Result<Message>(new Message(MessageId.New(), chatId, sentBy, text, time.GetUtcNow(), MessageState.New, isProposal, proposalId) { SetupTest = setupTest });
    }

    public void Propose()
    {
        State = MessageState.Proposed;
    }

    public void Queue()
    {
        State = MessageState.Queued;
    }

    public void Steer()
    {
        State = MessageState.Steering;
    }

    public void Deliver()
    {
        State = MessageState.Delivered;
    }

    public void Cancel()
    {
        State = MessageState.Cancelled;
    }

    public ChatMessage ToContract()
    {
        return new ChatMessage(Id, ChatId, SentBy, Text, SentAt, IsProposal);
    }
}
