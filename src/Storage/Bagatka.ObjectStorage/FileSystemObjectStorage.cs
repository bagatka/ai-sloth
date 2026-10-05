using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.ObjectStorage;

/// <summary>
/// Objects as files in a directory of this computer: for a host on one server, and for development.
/// A key's segments are its path. An object is written beside its file and moved over it, so a reader
/// sees the old object or the new one, whole.
/// </summary>
public sealed class FileSystemObjectStorage(FileSystemObjectStorageSettings settings) : IObjectStorage
{
    private const int BufferBytes = 81920;

    /// <inheritdoc />
    public async Task PutAsync(string key, Stream content, CancellationToken ct)
    {
        ObjectKeys.Check(key, nameof(key));
        ArgumentNullException.ThrowIfNull(content);
        string path = PathOf(key);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string written = path + "." + Guid.CreateVersion7().ToString("N") + ".new";
        try
        {
            await using (FileStream file = new FileStream(written, FileMode.CreateNew, FileAccess.Write, FileShare.None, BufferBytes, FileOptions.Asynchronous))
            {
                await content.CopyToAsync(file, ct).ConfigureAwait(false);
            }

            File.Move(written, path, overwrite: true);
        }
        finally
        {
            File.Delete(written);
        }
    }

    /// <inheritdoc />
    public Task<Stream?> OpenAsync(string key, CancellationToken ct)
    {
        ObjectKeys.Check(key, nameof(key));
        string path = PathOf(key);
        if (!File.Exists(path))
        {
            return Task.FromResult<Stream?>(null);
        }

        return Task.FromResult<Stream?>(new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferBytes, FileOptions.Asynchronous));
    }

    /// <inheritdoc />
    public Task DeleteAsync(string prefix, CancellationToken ct)
    {
        ObjectKeys.Check(prefix, nameof(prefix));
        string path = PathOf(prefix);
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }

        return Task.CompletedTask;
    }

    private string PathOf(string key)
    {
        return Path.Combine(settings.Root, key.Replace('/', Path.DirectorySeparatorChar));
    }
}
