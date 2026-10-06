using System;
using System.Collections.Generic;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// A disk image to make from a container image in a registry the service can pull from without
/// credentials, such as a public one on GitHub's or Microsoft's registry.
/// </summary>
public sealed class DiskImageCreateOptions
{
    /// <summary>Creates the options.</summary>
    /// <param name="imageReference">The container image, with its registry and tag or digest, such as <c>ghcr.io/owner/image:1.0</c>.</param>
    public DiskImageCreateOptions(string imageReference)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageReference);
        ImageReference = imageReference;
    }

    /// <summary>The container image.</summary>
    public string ImageReference { get; }

    /// <summary>A name for the disk image.</summary>
    public string? Name { get; set; }

    /// <summary>Labels to find it by.</summary>
    public IDictionary<string, string> Labels { get; } = new Dictionary<string, string>(StringComparer.Ordinal);
}
