using System.Collections.Generic;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// An image as the engine reports it.
/// </summary>
/// <param name="Id">The image ID.</param>
/// <param name="RepoTags">Its tags, such as <c>name:tag</c>; empty for an untagged (dangling) image.</param>
/// <param name="Environment">The environment variables containers inherit, as <c>NAME=value</c>.</param>
/// <param name="Labels">Its labels.</param>
public sealed record ImageDetails(
    string Id,
    IReadOnlyList<string> RepoTags,
    IReadOnlyList<string> Environment,
    IReadOnlyDictionary<string, string> Labels);
