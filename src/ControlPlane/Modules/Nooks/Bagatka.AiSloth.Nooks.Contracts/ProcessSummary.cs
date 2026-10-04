using System;
using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A process in a nook.
/// </summary>
/// <param name="Id">The process.</param>
/// <param name="NookId">The nook it runs in.</param>
/// <param name="Command">The program.</param>
/// <param name="Arguments">Its arguments.</param>
/// <param name="StartedAt">When the control plane asked the daemon to start it.</param>
/// <param name="ExitCode">Its exit code, or <see langword="null"/> while it runs.</param>
public sealed record ProcessSummary(
    ProcessId Id,
    NookId NookId,
    string Command,
    IReadOnlyList<string> Arguments,
    DateTimeOffset StartedAt,
    int? ExitCode);
