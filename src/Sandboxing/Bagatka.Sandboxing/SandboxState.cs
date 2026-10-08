namespace Bagatka.Sandboxing;

/// <summary>
/// A backend's view of a sandbox.
/// </summary>
public enum SandboxState
{
    /// <summary>Accepted; the image or snapshot is being prepared and started.</summary>
    Starting = 1,

    /// <summary>The entry point is running.</summary>
    Running = 2,

    /// <summary>Being suspended.</summary>
    Suspending = 3,

    /// <summary>Compute released with memory and files kept: processes continue on resume.</summary>
    Paused = 4,

    /// <summary>Compute released with files kept: processes start again on resume.</summary>
    Stopped = 5,


    /// <summary>The entry point stopped unexpectedly or never started; see <see cref="SandboxObservation.Reason"/>.</summary>
    Failed = 6,

    /// <summary>Being deleted.</summary>
    Deleting = 7,
}
