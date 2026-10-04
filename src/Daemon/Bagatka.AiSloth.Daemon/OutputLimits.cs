namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// How much process output the daemon keeps, and in what pieces.
/// </summary>
/// <param name="SegmentBytes">The size of each file output is kept in; whole segments are dropped.</param>
/// <param name="RecentBytes">Output kept for late watchers: the latest output for <c>Recent</c>, the latest delivered output for <c>Complete</c>.</param>
/// <param name="UndeliveredBytes">For <c>Complete</c>, how much undelivered output may wait before the process blocks.</param>
/// <param name="ChunkBytes">The most output read or sent at once.</param>
internal sealed record OutputLimits(int SegmentBytes, long RecentBytes, long UndeliveredBytes, int ChunkBytes)
{
    /// <summary>The limits the protocol promises.</summary>
    public static OutputLimits Default { get; } = new OutputLimits(
        SegmentBytes: 1 << 20,
        RecentBytes: 16L << 20,
        UndeliveredBytes: 64L << 20,
        ChunkBytes: 64 << 10);
}
