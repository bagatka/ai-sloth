using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Identifies a message in a chat.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<MessageId>))]
public readonly record struct MessageId : ITypedId<MessageId>
{
    private MessageId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static MessageId New()
    {
        return new MessageId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static MessageId From(Guid value)
    {
        return new MessageId(value);
    }
}
