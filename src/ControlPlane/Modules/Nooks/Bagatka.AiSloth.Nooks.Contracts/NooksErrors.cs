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

    /// <summary>The nook's setup is running; run it again once it ended.</summary>
    public static readonly Error SetupRunning = Error.Conflict("nooks.setup_running", "The nook's setup is running; wait for it to end.");

    /// <summary>
    /// The workspace has as many nooks awake as its host allows, and all of them are at work: another
    /// can start or wake once one of them finishes or is stopped.
    /// </summary>
    public static readonly Error TooManyAwake = Error.Conflict("nooks.too_many_awake", "All of the workspace's awake nooks are at work, and it may have no more awake; try again once one finishes.");

    /// <summary>
    /// The nook is reserved for someone else (<see cref="NookSummary.ReservedFor"/>): the actor may watch
    /// it, but not change what runs in it or its files.
    /// </summary>
    public static readonly Error Reserved = Error.NotAllowed("nooks.reserved", "This nook is reserved for the owner of the account its agent works on: only they may change what runs in it or its files.");

    /// <summary>The nook has no checkpoint with that number.</summary>
    public static readonly Error CheckpointNotFound = Error.NotFound("nooks.checkpoint_not_found", "The nook has no such checkpoint.");
}
