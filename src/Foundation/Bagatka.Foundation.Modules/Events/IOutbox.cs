namespace Bagatka.Foundation.Modules.Events;

/// <summary>
/// Where a module's integration events go (PATTERNS.md, entry 15). Entity methods that emit events
/// take it as a parameter and add them; they are saved with the change, in the same transaction, and
/// delivered to other modules' reactions after it commits.
/// </summary>
public interface IOutbox
{
    /// <summary>Adds the event, to be saved with the context's other changes.</summary>
    public void Add<TEvent>(TEvent integrationEvent)
        where TEvent : class;
}
