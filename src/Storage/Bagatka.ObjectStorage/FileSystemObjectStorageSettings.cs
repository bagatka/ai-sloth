using System;
using System.IO;

namespace Bagatka.ObjectStorage;

/// <summary>
/// Where <see cref="FileSystemObjectStorage"/> keeps objects.
/// </summary>
public sealed record FileSystemObjectStorageSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="root">An absolute path to a directory that only this storage writes in; it is created when missing.</param>
    public FileSystemObjectStorageSettings(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        if (!Path.IsPathFullyQualified(root))
        {
            throw new ArgumentException("The root must be an absolute path.", nameof(root));
        }

        Root = Path.GetFullPath(root);
    }

    /// <summary>The directory objects are kept in, one file per object.</summary>
    public string Root { get; }
}
