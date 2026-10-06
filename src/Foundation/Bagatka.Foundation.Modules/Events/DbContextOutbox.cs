using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.Foundation.Modules.Events;

/// <summary>
/// The outbox in a module's DbContext: <c>public IOutbox Outbox =&gt; new DbContextOutbox(Set&lt;OutboxMessage&gt;());</c>
/// Events become rows the context saves with its other changes.
/// </summary>
public sealed class DbContextOutbox(DbSet<OutboxMessage> messages) : IOutbox
{
    /// <inheritdoc />
    public void Add<TEvent>(TEvent integrationEvent)
        where TEvent : class
    {
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(messages);
        messages.Add(OutboxMessage.For(Reactions.TypeOf<TEvent>(), JsonSerializer.Serialize(integrationEvent, FoundationJson.Options)));
    }
}
