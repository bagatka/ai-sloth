using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Errors callers of <see cref="INooksApi"/> may branch on.
/// </summary>
public static class NooksErrors
{
    /// <summary>The nook doesn't exist, or the actor may not learn that it exists.</summary>
    public static readonly Error NotFound = Error.NotFound("nooks.not_found", "Nook not found.");

    /// <summary>The process doesn't exist in this nook.</summary>
    public static readonly Error ProcessNotFound = Error.NotFound("nooks.process_not_found", "Process not found.");

    /// <summary>The nook is still being created, or its daemon couldn't be reached in time; retry later.</summary>
    public static readonly Error NotReady = Error.Conflict("nooks.not_ready", "Nook is not ready.");

    /// <summary>The nook has no source by that name.</summary>
    public static readonly Error SourceNotFound = Error.NotFound("nooks.source_not_found", "The nook has no such source.");

    /// <summary>The nook has no checkpoint with that number.</summary>
    public static readonly Error CheckpointNotFound = Error.NotFound("nooks.checkpoint_not_found", "The nook has no such checkpoint.");
}
