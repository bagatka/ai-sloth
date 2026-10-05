using System;
using System.Text.Json;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Model;

// A chat event as saved: the kind names the body, and the data is the body as JSON. An agent
// update is kept as the agent sent it, in a jsonb column.
internal sealed class StoredEvent
{
    public const int MaxKindLength = 32;

    // Used by From and by EF: parameter names match property names.
    private StoredEvent(ChatId chatId, long sequence, DateTimeOffset at, string kind, string data)
    {
        ChatId = chatId;
        Sequence = sequence;
        At = at;
        Kind = kind;
        Data = data;
    }

    public ChatId ChatId { get; private set; }

    public long Sequence { get; private set; }

    public DateTimeOffset At { get; private set; }

    public string Kind { get; private set; }

    public string Data { get; private set; }

    public static StoredEvent From(ChatId chatId, long sequence, DateTimeOffset at, ChatEventBody body)
    {
        (string kind, string data) = body switch
        {
            MessageSent sent => ("message-sent", JsonSerializer.Serialize(sent, FoundationJson.Options)),
            MessageProposed proposed => ("message-proposed", JsonSerializer.Serialize(proposed, FoundationJson.Options)),
            TurnStarted started => ("turn-started", JsonSerializer.Serialize(started, FoundationJson.Options)),
            MessageSteered steered => ("message-steered", JsonSerializer.Serialize(steered, FoundationJson.Options)),
            MessageCancelled cancelled => ("message-cancelled", JsonSerializer.Serialize(cancelled, FoundationJson.Options)),
            AgentUpdate update => ("agent-update", update.Update.GetRawText()),
            TurnEnded ended => ("turn-ended", JsonSerializer.Serialize(ended, FoundationJson.Options)),
            CheckpointSaved saved => ("checkpoint-saved", JsonSerializer.Serialize(saved, FoundationJson.Options)),
            CheckpointFailed failed => ("checkpoint-failed", JsonSerializer.Serialize(failed, FoundationJson.Options)),
            AgentRestarted restarted => ("agent-restarted", JsonSerializer.Serialize(restarted, FoundationJson.Options)),
        };
        return new StoredEvent(chatId, sequence, at, kind, data);
    }

    public ChatEvent ToContract()
    {
        ChatEventBody body = Kind switch
        {
            "message-sent" => new ChatEventBody(Read<MessageSent>()),
            "message-proposed" => new ChatEventBody(Read<MessageProposed>()),
            "turn-started" => new ChatEventBody(Read<TurnStarted>()),
            "message-steered" => new ChatEventBody(Read<MessageSteered>()),
            "message-cancelled" => new ChatEventBody(Read<MessageCancelled>()),
            "agent-update" => new ChatEventBody(new AgentUpdate(JsonElement.Parse(Data))),
            "turn-ended" => new ChatEventBody(Read<TurnEnded>()),
            "checkpoint-saved" => new ChatEventBody(Read<CheckpointSaved>()),
            "checkpoint-failed" => new ChatEventBody(Read<CheckpointFailed>()),
            "agent-restarted" => new ChatEventBody(Read<AgentRestarted>()),
            _ => throw new InvalidOperationException("Chat " + ChatId.Value + " has an event of unknown kind " + Kind + "."),
        };
        return new ChatEvent(Sequence, At, body);
    }

    private T Read<T>()
    {
        T? body = JsonSerializer.Deserialize<T>(Data, FoundationJson.Options);
        if (body is null)
        {
            throw new InvalidOperationException("Chat " + ChatId.Value + " has an empty " + Kind + " event.");
        }

        return body;
    }
}
