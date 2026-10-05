namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Where a nook is in its lifecycle. A nook sleeps when nobody has used it for a while:
/// <see cref="Sleeping"/>, then <see cref="Paused"/> or <see cref="Stopped"/>, as its provider can, and
/// <see cref="Evicted"/> after a long sleep. Callers never have to act on any of them: any operation
/// wakes the nook first, so callers only notice latency. People see them all as asleep.
/// </summary>
public enum NookStatus
{
    /// <summary>Recorded; its provider is creating it, or will be asked again by reconciliation.</summary>
    Creating = 1,

    /// <summary>Running, with its daemon connected.</summary>
    Running = 2,

    /// <summary>Compute released with memory and files kept: resumes in about a second, and processes continue where they were.</summary>
    Paused = 3,

    /// <summary>Compute released with files kept: resumes in seconds, and processes start again.</summary>
    Stopped = 4,

    /// <summary>Its provider reports it running, but its daemon hasn't reconnected in time.</summary>
    Unreachable = 5,

    /// <summary>Its provider reports that it can't run.</summary>
    Failed = 6,

    /// <summary>Its provider is deleting it and its files.</summary>
    Deleting = 7,

    /// <summary>Going to sleep: its provider is releasing its compute.</summary>
    Sleeping = 8,

    /// <summary>
    /// Asleep so long that its sandbox was deleted; its files are in its latest checkpoint, and it comes
    /// back from there, starting again the way a new nook does.
    /// </summary>
    Evicted = 9,
}
