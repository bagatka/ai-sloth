using System.Collections.Generic;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// Settings for an image committed from a container.
/// </summary>
/// <param name="Environment">
/// Environment variables as <c>NAME=value</c>. The engine still adds every container variable whose
/// name isn't listed, so list a name with an empty value to keep its value out of the image.
/// </param>
/// <param name="Labels">Labels to attach; the container's other labels are added as well.</param>
public sealed record CommitConfiguration(
    IReadOnlyList<string> Environment,
    IReadOnlyDictionary<string, string> Labels);
