namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A process ended. A program that couldn't start exits with 127 and says why on standard error.
/// </summary>
/// <param name="ProcessId">The process.</param>
/// <param name="ExitCode">Its exit code; 0 means success.</param>
public sealed record ProcessExited(ProcessId ProcessId, int ExitCode);
