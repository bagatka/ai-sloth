using System;

namespace Bagatka.Foundation.Modules.Events;

/// <summary>
/// An event in a module's outbox, until every reaction to it succeeded. Modules map it in their
/// DbContext with <see cref="OutboxMessageConfiguration"/> and never touch it otherwise.
/// </summary>
public sealed class OutboxMessage
{
    private OutboxMessage(Guid id, string type, string payload, int attempts, DateTimeOffset? retryAt, DateTimeOffset? parkedAt)
    {
        Id = id;
        Type = type;
        Payload = payload;
        Attempts = attempts;
        RetryAt = retryAt;
        ParkedAt = parkedAt;
    }

    /// <summary>Time-ordered, so events are delivered oldest first.</summary>
    public Guid Id { get; private set; }

    /// <summary>The event's type, by its full name, which reactions are registered under.</summary>
    public string Type { get; private set; }

    /// <summary>The event as JSON.</summary>
    public string Payload { get; private set; }

    /// <summary>How many deliveries failed.</summary>
    public int Attempts { get; private set; }

    /// <summary>When the next delivery is due after a failed one; null while the first is due.</summary>
    public DateTimeOffset? RetryAt { get; private set; }

    /// <summary>When delivery stopped after repeated failures, for an operator to look at.</summary>
    public DateTimeOffset? ParkedAt { get; private set; }

    internal static OutboxMessage For(string type, string payload)
    {
        return new OutboxMessage(Guid.CreateVersion7(), type, payload, attempts: 0, retryAt: null, parkedAt: null);
    }

    // A delivery failed: the next one waits longer each time, and after the last attempt none follows.
    internal void Failed(int maxAttempts, Func<int, TimeSpan> backoff, DateTimeOffset now)
    {
        Attempts++;
        if (Attempts >= maxAttempts)
        {
            ParkedAt = now;
            return;
        }

        RetryAt = now + backoff(Attempts);
    }
}
