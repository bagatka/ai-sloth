using System;

namespace Bagatka.AiSloth.Cli;

// What the last chat started on a host ran with, which the next one uses unless told otherwise.
internal sealed record ChatDefaults(string Harness, Guid Account, string Provider);
