namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Where a nook is in its lifecycle, as people see it. Callers never have to act on it: any operation
/// wakes an asleep nook and waits for a starting one, so callers only notice latency.
/// </summary>
public enum NookStatus
{
    /// <summary>Being created, or woken: its provider starts it, and its daemon dials in.</summary>
    Starting = 1,

    /// <summary>Running, with its daemon connected.</summary>
    Ready = 2,

    /// <summary>Nobody used it for a while, so it uses no compute; its files are kept, and any use wakes it.</summary>
    Asleep = 3,

    /// <summary>Its daemon is away and its provider can't be asked, such as a machine that is turned off.</summary>
    Offline = 4,

    /// <summary>It can't run, such as when its provider reports it failed or its machine was removed.</summary>
    Failed = 5,

    /// <summary>Its provider is deleting it and its files.</summary>
    Deleting = 6,
}
