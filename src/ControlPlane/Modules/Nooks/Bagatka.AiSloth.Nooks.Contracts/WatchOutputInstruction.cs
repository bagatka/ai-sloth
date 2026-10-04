namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Upload a process's output from an offset, first what was kept and then live, in a separate upload
/// identified by the watch ID. The upload ends when the process exits and its output is delivered,
/// or when the control plane cancels it.
/// </summary>
/// <param name="WatchId">The watch the upload serves.</param>
/// <param name="ProcessId">The process.</param>
/// <param name="FromOffset">The output byte offset to start at.</param>
public sealed record WatchOutputInstruction(WatchId WatchId, ProcessId ProcessId, long FromOffset);
