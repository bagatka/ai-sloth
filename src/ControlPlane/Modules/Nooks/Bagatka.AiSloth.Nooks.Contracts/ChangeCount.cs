namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>How much a nook's repositories changed since they were copied in, all of them together.</summary>
/// <param name="Files">The files changed, added, removed, or renamed.</param>
/// <param name="Additions">The lines added.</param>
/// <param name="Deletions">The lines removed.</param>
public sealed record ChangeCount(int Files, int Additions, int Deletions);
