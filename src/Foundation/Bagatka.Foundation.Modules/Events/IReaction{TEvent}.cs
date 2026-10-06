using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.Foundation.Modules.Events;

/// <summary>
/// A module's handler for another module's integration event (PATTERNS.md, entry 15), registered
/// with <see cref="Reactions.AddReaction{TEvent, TReaction}"/>. Delivery is at least once, so a
/// reaction is idempotent; it commits only its own module's data. A failed result or an exception
/// has the event delivered again later.
/// </summary>
public interface IReaction<in TEvent>
    where TEvent : class
{
    /// <summary>Handles one event.</summary>
    public Task<Result> HandleAsync(TEvent integrationEvent, CancellationToken ct);
}
