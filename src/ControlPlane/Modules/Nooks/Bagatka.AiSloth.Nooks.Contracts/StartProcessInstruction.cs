using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Start a program directly, without a shell, and keep it running until it exits or is stopped,
/// independent of the daemon's connection. Its output is kept as the retention says, so it can be
/// watched from an offset.
/// </summary>
/// <param name="ProcessId">The ID the daemon reports the process under.</param>
/// <param name="Command">The program.</param>
/// <param name="Arguments">Its arguments, passed as they are.</param>
/// <param name="WorkingDirectory">The directory to start in, or <see langword="null"/> for the daemon's default.</param>
/// <param name="Retention">How its output is kept.</param>
public sealed record StartProcessInstruction(
    ProcessId ProcessId,
    string Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    OutputRetention Retention);
