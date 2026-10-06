using System;

namespace Bagatka.AiSloth.Cli;

// When sloth last looked for a newer release (update.json), so it looks at most once a day.
internal sealed record UpdateCheck(DateTimeOffset CheckedAt);
