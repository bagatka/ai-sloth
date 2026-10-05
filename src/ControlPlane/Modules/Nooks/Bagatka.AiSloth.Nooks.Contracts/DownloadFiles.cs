namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>Input to <see cref="INooksApi.DownloadAsync"/>.</summary>
/// <param name="NookId">The nook.</param>
/// <param name="Source">One of its sources, by name, or <see langword="null"/> for all of <c>/work</c>.</param>
/// <param name="Checkpoint">One of its checkpoints, by number, or <see langword="null"/> for its files as they are now.</param>
public sealed record DownloadFiles(NookId NookId, string? Source, int? Checkpoint);
