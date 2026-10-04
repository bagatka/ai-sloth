using System;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// Registers <see cref="DockerClient"/>.
/// </summary>
public static class DockerClientRegistration
{
    /// <summary>
    /// Registers one <see cref="DockerClient"/> for the whole application, disposed with the
    /// service provider. Call it once.
    /// </summary>
    public static IServiceCollection AddDockerClient(this IServiceCollection services, DockerClientSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        services.AddSingleton(_ => new DockerClient(settings));
        return services;
    }
}
