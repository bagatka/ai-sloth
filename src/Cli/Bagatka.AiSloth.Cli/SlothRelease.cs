using System;

namespace Bagatka.AiSloth.Cli;

// A released sloth's version, such as 0.2.0, and where GitHub's REST API lists its repository's releases.
internal sealed record SlothRelease(Version Version, Uri Releases);
