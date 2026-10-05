using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>Input to <see cref="INooksApi.CopyFilesOutAsync"/>.</summary>
/// <param name="NookId">The nook.</param>
/// <param name="Paths">Absolute paths of files or folders; those that don't exist are left out. At most 10.</param>
public sealed record CopyFilesOut(NookId NookId, IReadOnlyList<string> Paths);
