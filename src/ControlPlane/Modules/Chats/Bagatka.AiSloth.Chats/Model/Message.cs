using System;
using System.Globalization;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Model;

// A message someone sent to a chat's agent. Sending only records it; the chat's runner announces it
// and delivers it.
internal sealed class Message
{
    public const int MaxTextLength = 100_000;

    // Used by Send and by EF: parameter names match property names.
    private Message(MessageId id, ChatId chatId, UserId sentBy, string text, DateTimeOffset sentAt, MessageState state)
    {
        Id = id;
        ChatId = chatId;
        SentBy = sentBy;
        Text = text;
        SentAt = sentAt;
        State = state;
    }

    public MessageId Id { get; private set; }

    public ChatId ChatId { get; private set; }

    public UserId SentBy { get; private set; }

    public string Text { get; private set; }

    public DateTimeOffset SentAt { get; private set; }

    public MessageState State { get; private set; }

    public bool Waiting => State is MessageState.New or MessageState.Queued or MessageState.Steering;

    public static Result<Message> Send(ChatId chatId, UserId sentBy, string? text, TimeProvider time)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxTextLength)
        {
            string message = string.Create(CultureInfo.InvariantCulture, $"Must be 1 to {MaxTextLength} characters, not only spaces.");
            return new Result<Message>(Error.Validation("text", message));
        }

        return new Result<Message>(new Message(MessageId.New(), chatId, sentBy, text, time.GetUtcNow(), MessageState.New));
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
        return new ChatMessage(Id, ChatId, SentBy, Text, SentAt);
    }
}
