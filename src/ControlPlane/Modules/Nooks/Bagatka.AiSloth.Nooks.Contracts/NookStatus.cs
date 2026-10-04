namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Where a nook is in its lifecycle. Callers never have to act on <see cref="Paused"/> or
/// <see cref="Stopped"/>: any operation resumes the nook first.
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
}
