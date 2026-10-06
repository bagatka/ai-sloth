using System;
using System.Collections.Generic;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// A disk image sandboxes start from: made from a container image, or committed from a sandbox.
/// </summary>
public sealed class DiskImage
{
    internal DiskImage(string id, string? name, IReadOnlyDictionary<string, string> labels, string? baseImage, DiskImageState state, string? errorMessage, DateTimeOffset? createdAt, long? sizeInMegabytes)
    {
        Id = id;
        Name = name;
        Labels = labels;
        BaseImage = baseImage;
        State = state;
        ErrorMessage = errorMessage;
        CreatedAt = createdAt;
        SizeInMegabytes = sizeInMegabytes;
    }

    /// <summary>The disk image's ID, a GUID the service chose.</summary>
    public string Id { get; }

    /// <summary>Its name, if it was given one.</summary>
    public string? Name { get; }

    /// <summary>Its labels.</summary>
    public IReadOnlyDictionary<string, string> Labels { get; }

    /// <summary>The container image it was made from, such as <c>ghcr.io/owner/image:tag</c>.</summary>
    public string? BaseImage { get; }

    /// <summary>Whether it can start sandboxes yet.</summary>
    public DiskImageState State { get; }

    /// <summary>Why making it failed, when it did.</summary>
    public string? ErrorMessage { get; }

    /// <summary>When it was created.</summary>
    public DateTimeOffset? CreatedAt { get; }

    /// <summary>Its size, in megabytes.</summary>
    public long? SizeInMegabytes { get; }
}
