using System;

namespace Bagatka.ObjectStorage.AzureBlob;

/// <summary>
/// Where <see cref="AzureBlobObjectStorageRegistration.AddAzureBlobObjectStorage"/> keeps objects.
/// </summary>
public sealed record AzureBlobObjectStorageSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="containerUrl">
    /// The blob container objects are kept in, such as <c>https://account.blob.core.windows.net/objects</c>,
    /// which only this storage writes in; it is created when missing.
    /// </param>
    public AzureBlobObjectStorageSettings(Uri containerUrl)
    {
        ArgumentNullException.ThrowIfNull(containerUrl);
        if (!containerUrl.IsAbsoluteUri || !string.Equals(containerUrl.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal))
        {
            throw new ArgumentException("The container URL must be an absolute https URL.", nameof(containerUrl));
        }

        ContainerUrl = containerUrl;
    }

    /// <summary>The blob container objects are kept in.</summary>
    public Uri ContainerUrl { get; }
}
