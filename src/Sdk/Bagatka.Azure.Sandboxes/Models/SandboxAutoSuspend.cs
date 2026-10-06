using System;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// Whether the service suspends a sandbox by itself once it was idle for a while, and what the
/// suspended sandbox keeps.
/// </summary>
public sealed class SandboxAutoSuspend
{
    private SandboxAutoSuspend(bool enabled, TimeSpan? after, SandboxSuspendMode? mode)
    {
        Enabled = enabled;
        After = after;
        Mode = mode;
    }

    /// <summary>The service never suspends the sandbox by itself.</summary>
    public static SandboxAutoSuspend Disabled { get; } = new SandboxAutoSuspend(enabled: false, after: null, mode: null);

    /// <summary>Whether the service suspends the sandbox by itself.</summary>
    public bool Enabled { get; }

    /// <summary>How long the sandbox is idle before the service suspends it, in whole seconds.</summary>
    public TimeSpan? After { get; }

    /// <summary>What the suspended sandbox keeps.</summary>
    public SandboxSuspendMode? Mode { get; }

    /// <summary>The service suspends the sandbox after it was idle this long, keeping what the mode says.</summary>
    /// <param name="after">At least a second, in whole seconds.</param>
    /// <param name="mode">What the suspended sandbox keeps.</param>
    public static SandboxAutoSuspend AfterIdle(TimeSpan after, SandboxSuspendMode mode)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(after, TimeSpan.FromSeconds(1));
        return new SandboxAutoSuspend(enabled: true, after, mode);
    }
}
