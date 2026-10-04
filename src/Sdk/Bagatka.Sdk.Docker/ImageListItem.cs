using System;
using System.Collections.Generic;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// A tagged image as the engine lists it.
/// </summary>
/// <param name="Id">The image ID.</param>
/// <param name="RepoTags">Its tags, such as <c>name:tag</c>.</param>
/// <param name="Labels">Its labels.</param>
/// <param name="Created">When it was created.</param>
public sealed record ImageListItem(
    string Id,
    IReadOnlyList<string> RepoTags,
    IReadOnlyDictionary<string, string> Labels,
    DateTimeOffset Created);
