namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A process ended. A program that couldn't start exits with 127 and says why on standard error; a
/// process lost with its nook's sandbox or daemon, whose exit nobody saw, with <see cref="Lost"/>.
/// </summary>
/// <param name="ProcessId">The process.</param>
/// <param name="ExitCode">Its exit code; 0 means success.</param>
public sealed record ProcessExited(ProcessId ProcessId, int ExitCode)
{
    /// <summary>The exit code of a process lost with its nook's sandbox or daemon.</summary>
    public const int Lost = -1;
}
