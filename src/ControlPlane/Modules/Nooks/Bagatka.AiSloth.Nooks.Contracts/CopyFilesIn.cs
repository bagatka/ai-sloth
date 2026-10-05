using System.Collections.Generic;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>Input to <see cref="INooksApi.CopyFilesInAsync"/>.</summary>
/// <param name="NookId">The nook.</param>
/// <param name="Replacing">
/// Absolute paths removed before the archive is unpacked, such as a folder it holds all of, so files
/// it no longer has go too; at most 10. Empty to only add and replace files.
/// </param>
public sealed record CopyFilesIn(NookId NookId, IReadOnlyList<string> Replacing);
