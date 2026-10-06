using System;
using Azure.Core;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.ObjectStorage.AzureBlob;

/// <summary>
/// Registers an <see cref="IObjectStorage"/> in Azure Blob Storage.
/// </summary>
public static class AzureBlobObjectStorageRegistration
{
    /// <summary>Registers objects kept as blobs in a container. Call it once.</summary>
    /// <param name="services">The services.</param>
    /// <param name="settings">The container.</param>
    /// <param name="credential">
    /// Signs the storage in, such as a managed identity when hosted; it needs the Storage Blob Data
    /// Contributor role on the container or its account.
    /// </param>
    public static IServiceCollection AddAzureBlobObjectStorage(this IServiceCollection services, AzureBlobObjectStorageSettings settings, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        services.AddSingleton<IObjectStorage>(new AzureBlobObjectStorage(new BlobContainerClient(settings.ContainerUrl, credential)));
        return services;
    }
}
