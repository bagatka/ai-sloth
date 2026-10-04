namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Something a running process did: wrote output, or exited. A process's events arrive in order,
/// and <see cref="ProcessExited"/> is always its last.
/// </summary>
public union ProcessEvent(ProcessOutput, ProcessExited);
