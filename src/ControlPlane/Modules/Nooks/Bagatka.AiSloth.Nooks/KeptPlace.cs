using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks;

// A place of a checkpoint, and the bundles that bring its commit, oldest first.
internal sealed record KeptPlace(string Path, string Commit, IReadOnlyList<string> Bundles);
