using System.Collections.Generic;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks;

// A run of a nook's setup as it started: its scripts, relative to /work, and the process running
// them; none when it has no scripts.
internal sealed record SetupStart(List<string> Scripts, ProcessId? Process);
