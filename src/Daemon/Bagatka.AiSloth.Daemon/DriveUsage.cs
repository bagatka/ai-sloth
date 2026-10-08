namespace Bagatka.AiSloth.Daemon;

/// <summary>How full a disk is.</summary>
/// <param name="TotalBytes">Its size.</param>
/// <param name="AvailableBytes">The space still free.</param>
internal sealed record DriveUsage(long TotalBytes, long AvailableBytes);
