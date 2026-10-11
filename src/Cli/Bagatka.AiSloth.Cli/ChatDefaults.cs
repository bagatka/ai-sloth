using System;

namespace Bagatka.AiSloth.Cli;

// What the last chat started on a host ran with, which the next one uses unless told otherwise: its
// model and thinking level only with the same harness and account.
internal sealed record ChatDefaults(string Harness, Guid Account, string Provider, string? Model = null, string? Thinking = null);
