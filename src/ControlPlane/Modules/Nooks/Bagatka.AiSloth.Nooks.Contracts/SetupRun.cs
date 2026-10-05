using System;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A run of a nook's setup scripts: one process, whose output says what each script did. It fails
/// with the first failing script's exit code, 124 for one that ran out of time, after the others ran.
/// </summary>
/// <param name="Process">The process, to watch its output.</param>
/// <param name="StartedAt">When it started.</param>
/// <param name="EndedAt">When it ended, or <see langword="null"/> while it runs.</param>
/// <param name="ExitCode">How it ended, or <see langword="null"/> while it runs: 0 when every script succeeded.</param>
public sealed record SetupRun(ProcessId Process, DateTimeOffset StartedAt, DateTimeOffset? EndedAt, int? ExitCode);
