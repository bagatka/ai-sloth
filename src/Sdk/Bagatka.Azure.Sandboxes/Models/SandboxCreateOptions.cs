using System;
using System.Collections.Generic;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// What to create a sandbox from and with. The service runs the container image's entry point,
/// command, and environment, as Docker would, unless overridden here.
/// </summary>
public sealed class SandboxCreateOptions
{
    /// <summary>Creates the options.</summary>
    /// <param name="source">What the sandbox starts from.</param>
    /// <param name="resources">The CPU, memory, and disk it gets.</param>
    public SandboxCreateOptions(SandboxSource source, SandboxResources resources)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(resources);
        Source = source;
        Resources = resources;
    }

    /// <summary>What the sandbox starts from.</summary>
    public SandboxSource Source { get; }

    /// <summary>The CPU, memory, and disk it gets.</summary>
    public SandboxResources Resources { get; }

    /// <summary>Labels to find it by; ignored by the service for a snapshot.</summary>
    public IDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>Environment variables for its entry point; never returned by the service. Ignored for a snapshot.</summary>
    public IDictionary<string, string> Environment { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>The program to run instead of the image's entry point, and its fixed arguments; empty for the image's own.</summary>
    public IList<string> Entrypoint { get; } = [];

    /// <summary>The arguments after the entry point instead of the image's command; empty for the image's own.</summary>
    public IList<string> Command { get; } = [];

    /// <summary>When the service suspends it by itself; <see langword="null"/> for the service's default.</summary>
    public SandboxAutoSuspend? AutoSuspend { get; set; }
}
