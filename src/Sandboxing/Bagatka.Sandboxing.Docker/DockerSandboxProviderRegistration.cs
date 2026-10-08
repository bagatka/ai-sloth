using System;
using Bagatka.Sdk.Docker;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Sandboxing.Docker;

/// <summary>
/// Registers the Docker sandbox provider.
/// </summary>
public static class DockerSandboxProviderRegistration
{
    /// <summary>
    /// Registers an <see cref="ISandboxProvider"/> named <c>docker</c> that runs sandboxes as
    /// containers on a Docker Engine, together with the <see cref="DockerClient"/> it uses.
    /// </summary>
    public static IServiceCollection AddDockerSandboxProvider(this IServiceCollection services, DockerSandboxSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        services.AddDockerClient(settings.Client);
        services.AddSingleton<ISandboxProvider>(provider =>
            new DockerSandboxProvider(provider.GetRequiredService<DockerClient>(), settings.Scope, settings.HostPorts));
        return services;
    }
}
