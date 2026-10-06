using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Bagatka.ObjectStorage.AzureBlob;
using Xunit;

namespace Bagatka.ObjectStorage.Tests;

/// <summary>
/// The guarantees of <see cref="IObjectStorage"/>, which every backend keeps: the folder one, and Azure
/// Blob Storage's against its emulator. Each test works in a store of its own.
/// </summary>
public sealed class ObjectStorageTests(Azurite azurite) : IDisposable
{
    private readonly DirectoryInfo _folder = Directory.CreateTempSubdirectory("objects-");

    public static TheoryData<string> Backends => new TheoryData<string>(["folder", "azure-blob"]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task An_object_reads_back_whole_with_its_length_and_a_new_one_replaces_it(string backend)
    {
        IObjectStorage storage = Storage(backend);
        await storage.PutAsync("nooks/abc/checkpoints/1/api.bundle", Content("first"), Ct);
        await storage.PutAsync("nooks/abc/checkpoints/1/api.bundle", Content("second, longer"), Ct);

        await using Stream? read = await storage.OpenAsync("nooks/abc/checkpoints/1/api.bundle", Ct);
        Assert.NotNull(read);
        using StreamReader reader = new StreamReader(read);
        string text = await reader.ReadToEndAsync(Ct);

        Assert.Equal("second, longer", text);
        Assert.Equal(Encoding.UTF8.GetByteCount("second, longer"), read.Length);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_missing_object_is_none(string backend)
    {
        IObjectStorage storage = Storage(backend);

        Stream? read = await storage.OpenAsync("nooks/missing/checkpoints/1/api.bundle", Ct);

        Assert.Null(read);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task Deleting_a_prefix_deletes_what_is_under_it_and_nothing_else(string backend)
    {
        IObjectStorage storage = Storage(backend);
        await storage.PutAsync("nooks/abc/checkpoints/1/api.bundle", Content("a"), Ct);
        await storage.PutAsync("nooks/abc/checkpoints/2/api.bundle", Content("b"), Ct);
        await storage.PutAsync("nooks/abcd/checkpoints/1/api.bundle", Content("c"), Ct);

        await storage.DeleteAsync("nooks/abc", Ct);
        await storage.DeleteAsync("nooks/never-written", Ct);
        Stream? gone = await storage.OpenAsync("nooks/abc/checkpoints/2/api.bundle", Ct);
        await using Stream? kept = await storage.OpenAsync("nooks/abcd/checkpoints/1/api.bundle", Ct);

        Assert.Null(gone);
        Assert.NotNull(kept);
    }

    [Theory]
    [MemberData(nameof(Backends))]
    public async Task A_key_outside_the_allowed_characters_is_refused(string backend)
    {
        IObjectStorage storage = Storage(backend);

        await Assert.ThrowsAsync<ArgumentException>(async () => await storage.PutAsync("nooks/../secrets", Content("x"), Ct));
        await Assert.ThrowsAsync<ArgumentException>(async () => await storage.OpenAsync("Nooks/ABC", Ct));
    }

    public void Dispose()
    {
        _folder.Delete(recursive: true);
    }

    private IObjectStorage Storage(string backend)
    {
        return backend switch
        {
            "folder" => new FileSystemObjectStorage(new FileSystemObjectStorageSettings(_folder.FullName)),
            "azure-blob" => new AzureBlobObjectStorage(new BlobContainerClient(azurite.ConnectionString, "objects-" + RandomNumberGenerator.GetHexString(12, lowercase: true))),
            _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "No backend has this name."),
        };
    }

    private static MemoryStream Content(string text)
    {
        return new MemoryStream(Encoding.UTF8.GetBytes(text));
    }
}
