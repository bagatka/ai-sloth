using System;

namespace Bagatka.Sandboxing;

/// <summary>
/// The caller's identifier for a sandbox. Providers derive resource names and tags from it, which
/// is what makes every operation safe to repeat.
/// </summary>
public readonly record struct SandboxKey
{
    private SandboxKey(Guid value)
    {
        Value = value;
    }

    /// <summary>The raw value.</summary>
    public Guid Value { get; }

    /// <summary>Wraps the caller's own sandbox identifier.</summary>
    public static SandboxKey From(Guid value)
    {
        return new SandboxKey(value);
    }
}
