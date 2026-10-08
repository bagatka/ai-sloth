using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Foundation.Modules.Events;

/// <summary>Registers integration event delivery (PATTERNS.md, entry 15).</summary>
public static class Reactions
{
    /// <summary>
    /// Registers a reaction to another module's event, in the reacting module's registration.
    /// </summary>
    public static IServiceCollection AddReaction<TEvent, TReaction>(this IServiceCollection services)
        where TEvent : class
        where TReaction : class, IReaction<TEvent>
    {
        services.AddSingleton<TReaction>();
        services.AddSingleton(new Reaction(TypeOf<TEvent>(), typeof(TReaction).Name, DeliverAsync<TEvent, TReaction>));
        return services;
    }

    /// <summary>
    /// Delivers the events the module's entities add to its DbContext's outbox, in the publishing
    /// module's registration. The context maps <see cref="OutboxMessage"/> with
    /// <see cref="OutboxMessageConfiguration"/>.
    /// </summary>
    public static IServiceCollection AddOutbox<TDbContext>(this IServiceCollection services)
        where TDbContext : DbContext
    {
        services.AddHostedService<OutboxDispatcher<TDbContext>>();
        return services;
    }

    // The name an event is stored and delivered under. It is persisted only until delivery, so a
    // renamed event type loses only events published before a deploy and delivered after it.
    internal static string TypeOf<TEvent>()
    {
        string? name = typeof(TEvent).FullName;
        if (name is null)
        {
            throw new InvalidOperationException("An event type needs a full name.");
        }

        return name;
    }

    private static async Task<Result> DeliverAsync<TEvent, TReaction>(IServiceProvider services, string payload, CancellationToken ct)
        where TEvent : class
        where TReaction : class, IReaction<TEvent>
    {
        TEvent? integrationEvent = JsonSerializer.Deserialize<TEvent>(payload, FoundationJson.Options);
        if (integrationEvent is null)
        {
            throw new InvalidOperationException("The outbox holds a null " + TypeOf<TEvent>() + ".");
        }

        return await services.GetRequiredService<TReaction>().HandleAsync(integrationEvent, ct);
    }
}
