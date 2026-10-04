namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// How full a nook's disk is, as its daemon measured it. On the Docker provider this is the host's
/// disk, because Docker can't cap a container's disk on most setups.
/// </summary>
/// <param name="TotalBytes">The disk's size.</param>
/// <param name="AvailableBytes">The space still free for the nook's files.</param>
public sealed record DiskUsage(long TotalBytes, long AvailableBytes);
