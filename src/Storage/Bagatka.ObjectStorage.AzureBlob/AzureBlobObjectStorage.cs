using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace Bagatka.ObjectStorage.AzureBlob;

/// <summary>
/// Objects as blobs in an Azure Blob Storage container, named by their keys: for a hosted control
/// plane. Uploading a blob replaces it whole, so a reader sees the old object or the new one.
/// </summary>
internal sealed class AzureBlobObjectStorage(BlobContainerClient container) : IObjectStorage
{
    private int _created;

    public async Task PutAsync(string key, Stream content, CancellationToken ct)
    {
        ObjectKeys.Check(key, nameof(key));
        await EnsureContainerAsync(ct).ConfigureAwait(false);
        await container.GetBlobClient(key).UploadAsync(content, overwrite: true, ct).ConfigureAwait(false);
    }

    public async Task<Stream?> OpenAsync(string key, CancellationToken ct)
    {
        ObjectKeys.Check(key, nameof(key));
        try
        {
            // Reads in chunks as the caller reads, and knows its length.
            return await container.GetBlobClient(key).OpenReadAsync(new BlobOpenReadOptions(allowModifications: false), ct).ConfigureAwait(false);
        }
        catch (RequestFailedException failed) when (failed.Status == 404)
        {
            return null;
        }
    }

    public async Task DeleteAsync(string prefix, CancellationToken ct)
    {
        ObjectKeys.Check(prefix, nameof(prefix));
        try
        {
            await foreach (BlobItem blob in container.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix + "/", ct).ConfigureAwait(false))
            {
                await container.DeleteBlobIfExistsAsync(blob.Name, DeleteSnapshotsOption.IncludeSnapshots, conditions: null, ct).ConfigureAwait(false);
            }
        }
        catch (RequestFailedException failed) when (failed.ErrorCode == BlobErrorCode.ContainerNotFound)
        {
            // Nothing was ever kept.
        }
    }

    // The container is made on the first write, once per process.
    private async Task EnsureContainerAsync(CancellationToken ct)
    {
        if (Volatile.Read(ref _created) == 1)
        {
            return;
        }

        await container.CreateIfNotExistsAsync(PublicAccessType.None, metadata: null, ct).ConfigureAwait(false);
        Volatile.Write(ref _created, 1);
    }
}
