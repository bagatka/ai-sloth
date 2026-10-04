namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A process the daemon reports as still running when it connects.
/// </summary>
/// <param name="ProcessId">The process.</param>
/// <param name="OutputLength">How many bytes of output it has written so far.</param>
public sealed record RunningProcess(ProcessId ProcessId, long OutputLength);
