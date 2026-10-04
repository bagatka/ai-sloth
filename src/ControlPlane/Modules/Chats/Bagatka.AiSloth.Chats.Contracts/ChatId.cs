using System;
using System.Text.Json.Serialization;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// Identifies a chat.
/// </summary>
[JsonConverter(typeof(TypedIdJsonConverter<ChatId>))]
public readonly record struct ChatId : ITypedId<ChatId>
{
    private ChatId(Guid value)
    {
        Value = value;
    }

    /// <inheritdoc />
    public Guid Value { get; }

    /// <summary>Creates a new, time-ordered (UUIDv7) ID.</summary>
    public static ChatId New()
    {
        return new ChatId(Guid.CreateVersion7());
    }

    /// <inheritdoc />
    public static ChatId From(Guid value)
    {
        return new ChatId(value);
    }
}
