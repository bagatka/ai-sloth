namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Stop a process: first politely, then forcibly. Its exit is still reported.
/// </summary>
/// <param name="ProcessId">The process.</param>
public sealed record StopProcessInstruction(ProcessId ProcessId);
