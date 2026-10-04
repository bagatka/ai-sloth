using System;

namespace Bagatka.Foundation;

/// <summary>
/// An <see cref="Actor"/> that is a background process, such as a reaction or a job.
/// </summary>
public sealed record SystemActor
{
    /// <summary>Creates a system actor.</summary>
    /// <param name="name">The process name, <c>&lt;module&gt;.&lt;process&gt;</c>.</param>
    public SystemActor(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
    }

    /// <summary>The process name, <c>&lt;module&gt;.&lt;process&gt;</c>.</summary>
    public string Name { get; }
}
