using System;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>A folder AiSloth keeps outside every nook (<see cref="INooksApi.SyncFolderAsync"/>).</summary>
/// <param name="Folder">Its name.</param>
/// <param name="SavedAt">When a nook last saved changes to it.</param>
/// <param name="Bytes">How much its history takes.</param>
/// <param name="SavedBy">The nook that last saved changes to it.</param>
public sealed record KeptFolderSummary(string Folder, DateTimeOffset SavedAt, long Bytes, NookId SavedBy);
