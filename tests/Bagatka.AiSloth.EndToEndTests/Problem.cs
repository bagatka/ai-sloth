using System.Collections.Generic;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>The problem details an error becomes: its code, and for validation, the rejected fields.</summary>
internal sealed record Problem(string? Code = null, IReadOnlyDictionary<string, string[]>? Errors = null);
