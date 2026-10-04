namespace Bagatka.Sandboxing;

/// <summary>
/// The compute a sandbox gets. A provider that can't offer exactly this rejects the spec rather
/// than silently giving more or less.
/// </summary>
/// <param name="CpuMillicores">CPU in thousandths of a core: 500 is half a core.</param>
/// <param name="MemoryMebibytes">Memory in MiB.</param>
public sealed record SandboxResources(int CpuMillicores, int MemoryMebibytes);
