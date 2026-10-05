using System;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.ObjectStorage;

/// <summary>
/// Registers an <see cref="IObjectStorage"/>.
/// </summary>
public static class ObjectStorageRegistration
{
    /// <summary>Registers objects kept as files in a directory of this computer. Call it once.</summary>
    public static IServiceCollection AddFileSystemObjectStorage(this IServiceCollection services, FileSystemObjectStorageSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        services.AddSingleton<IObjectStorage>(new FileSystemObjectStorage(settings));
        return services;
    }
}
