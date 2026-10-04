using System;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// One thing that happened in a chat. Sequence numbers start at 1 and grow by one, so a watcher
/// that saw sequence <c>n</c> resumes after it without gaps or repeats.
/// </summary>
/// <param name="Sequence">Its place in the chat.</param>
/// <param name="At">When it happened.</param>
/// <param name="Body">What happened.</param>
public sealed record ChatEvent(long Sequence, DateTimeOffset At, ChatEventBody Body);
