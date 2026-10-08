namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// How much of its disk, memory, and CPU a nook uses, as its daemon last measured it, every 30
/// seconds while it runs. A figure its daemon can't measure is 0. On the Docker provider the disk is
/// the host's, because Docker can't cap a container's disk on most setups.
/// </summary>
/// <param name="DiskUsedBytes">The disk space the nook's files use.</param>
/// <param name="DiskTotalBytes">The disk's size.</param>
/// <param name="MemoryUsedBytes">The memory its processes use.</param>
/// <param name="MemoryTotalBytes">The memory it may use.</param>
/// <param name="CpuUsedMillicores">The CPU its processes used since the last measurement, in thousandths of a core.</param>
/// <param name="CpuTotalMillicores">The CPU it may use, in thousandths of a core.</param>
public sealed record NookUsage(
    long DiskUsedBytes,
    long DiskTotalBytes,
    long MemoryUsedBytes,
    long MemoryTotalBytes,
    int CpuUsedMillicores,
    int CpuTotalMillicores);
