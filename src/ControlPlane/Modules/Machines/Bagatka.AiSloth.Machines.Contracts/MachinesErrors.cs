using Bagatka.Foundation;

namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// Errors callers of <see cref="IMachinesApi"/> may branch on.
/// </summary>
public static class MachinesErrors
{
    /// <summary>The machine doesn't exist, or the actor may not learn that it exists.</summary>
    public static readonly Error NotFound = Error.NotFound("machines.not_found", "Machine not found.");
}
