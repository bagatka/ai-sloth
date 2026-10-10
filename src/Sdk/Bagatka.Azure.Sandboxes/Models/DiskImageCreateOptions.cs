using System;
using System.Collections.Generic;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// A disk image to make from a container image: one in a public registry, such as GitHub's or
/// Microsoft's, or in a private one the service signs in to with <see cref="RegistryCredentials"/>.
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

    /// <summary>What the service signs in to the image's registry with, for a private registry; none for a public one.</summary>
    public RegistryCredentials? RegistryCredentials { get; set; }
}
