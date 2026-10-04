using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INooksApi.StartProcessAsync"/>. The program runs directly, not through a
/// shell, so arguments are never re-parsed.
/// </summary>
/// <param name="NookId">The nook to run in.</param>
/// <param name="Command">The program, such as <c>git</c> or <c>/bin/sh</c>.</param>
/// <param name="Arguments">Its arguments, passed as they are.</param>
/// <param name="WorkingDirectory">The directory to start in, or <see langword="null"/> for the daemon's default.</param>
/// <param name="Retention">How its output is kept: <see cref="OutputRetention.Recent"/> unless every byte matters.</param>
/// <param name="Environment">
/// Variables added to the process's environment. They may hold secrets: they reach the daemon and
/// are never stored or logged. Names starting with <c>SLOTHD_</c> are the daemon's own.
/// </param>
public sealed record StartProcess(
    NookId NookId,
    string Command,
    IReadOnlyList<string> Arguments,
    string? WorkingDirectory,
    OutputRetention Retention,
    IReadOnlyDictionary<string, string> Environment);
