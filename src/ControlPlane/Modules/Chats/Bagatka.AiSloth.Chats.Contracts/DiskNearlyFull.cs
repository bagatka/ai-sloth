namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// After a turn, the chat's nook's disk is nearly full, so the agent's work and the checkpoints that
/// keep its files may soon fail: people should free space, or move to a nook with a bigger disk.
/// </summary>
/// <param name="UsedBytes">The disk space the nook's files use.</param>
/// <param name="TotalBytes">The disk's size.</param>
public sealed record DiskNearlyFull(long UsedBytes, long TotalBytes);
