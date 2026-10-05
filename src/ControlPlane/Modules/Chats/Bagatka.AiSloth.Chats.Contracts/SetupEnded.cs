using System;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// The project's setup ended. The agent starts either way; after a failure it knows, and can read
/// the whole output in <c>/var/log/aisloth/setup.log</c>.
/// </summary>
/// <param name="ExitCode">0 when every script succeeded; otherwise the first failing script's, 124 for one that ran out of time.</param>
/// <param name="Took">How long it ran.</param>
/// <param name="Output">The end of its output when it failed, at most 2,000 characters; <see langword="null"/> when it succeeded.</param>
public sealed record SetupEnded(int ExitCode, TimeSpan Took, string? Output);
