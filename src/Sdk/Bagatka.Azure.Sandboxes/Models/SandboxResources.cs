using System;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// The CPU, memory, and disk a sandbox gets, as Kubernetes-style quantities, such as <c>500m</c> CPU,
/// <c>2Gi</c> memory, and <c>32Gi</c> disk.
/// </summary>
public sealed class SandboxResources
{
    /// <summary>Creates the resources.</summary>
    /// <param name="cpu">The CPU, such as <c>500m</c> or <c>2</c>.</param>
    /// <param name="memory">The memory, such as <c>2048Mi</c> or <c>4Gi</c>.</param>
    /// <param name="disk">The disk, such as <c>32Gi</c>; <see langword="null"/> for the service's default.</param>
    public SandboxResources(string cpu, string memory, string? disk = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cpu);
        ArgumentException.ThrowIfNullOrWhiteSpace(memory);
        Cpu = cpu;
        Memory = memory;
        Disk = disk;
    }

    /// <summary>The CPU.</summary>
    public string Cpu { get; }

    /// <summary>The memory.</summary>
    public string Memory { get; }

    /// <summary>The disk; <see langword="null"/> for the service's default.</summary>
    public string? Disk { get; }
}
