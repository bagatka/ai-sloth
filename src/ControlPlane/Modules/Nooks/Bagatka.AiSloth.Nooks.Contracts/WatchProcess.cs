namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INooksApi.WatchProcessAsync"/>.
/// </summary>
/// <param name="NookId">The nook.</param>
/// <param name="ProcessId">The process.</param>
/// <param name="FromOffset">The output byte offset to start at: 0 for everything, or the offset after the last byte already seen.</param>
public sealed record WatchProcess(NookId NookId, ProcessId ProcessId, long FromOffset);
