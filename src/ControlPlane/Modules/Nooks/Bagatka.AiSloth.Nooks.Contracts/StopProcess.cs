namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INooksApi.StopProcessAsync"/>.
/// </summary>
/// <param name="NookId">The nook.</param>
/// <param name="ProcessId">The process.</param>
public sealed record StopProcess(NookId NookId, ProcessId ProcessId);
