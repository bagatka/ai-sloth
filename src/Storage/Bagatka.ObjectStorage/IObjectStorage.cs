using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.ObjectStorage;

/// <summary>
/// Objects by key: write once, read many times, delete by prefix. A key is segments of lowercase
/// letters, digits, <c>.</c>, <c>_</c>, and <c>-</c>, separated by <c>/</c>, such as
/// <c>nooks/0199b3a4/checkpoints/3/api.bundle</c>. Thread-safe.
/// </summary>
public interface IObjectStorage
{
    /// <summary>
    /// Stores <paramref name="content"/> under <paramref name="key"/>, replacing what was there: the
    /// whole object or, should it fail, nothing new.
    /// </summary>
    /// <exception cref="System.ArgumentException">The key isn't a valid key.</exception>
    public Task PutAsync(string key, Stream content, CancellationToken ct);

    /// <summary>
    /// The object's content to read, whose <see cref="Stream.Length"/> is the object's size; the caller
    /// disposes it. <see langword="null"/> when there's none.
    /// </summary>
    public Task<Stream?> OpenAsync(string key, CancellationToken ct);

    /// <summary>Deletes every object whose key starts with <paramref name="prefix"/> and a <c>/</c>, such as everything under <c>nooks/0199b3a4</c>.</summary>
    public Task DeleteAsync(string prefix, CancellationToken ct);
}
