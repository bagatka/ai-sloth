using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks;

// The actors this module's own work runs as (PATTERNS.md, entry 12).
internal static class SystemActors
{
    // Starts processes in nooks, for whoever Nooks already let start them.
    public static readonly Actor Processes = Actor.ForSystem("nooks.processes");
}
