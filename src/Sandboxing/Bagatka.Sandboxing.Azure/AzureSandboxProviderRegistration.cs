using System;
using Azure.Core;
using Bagatka.Azure.Sandboxes;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Sandboxing.Azure;

/// <summary>
/// Registers the Azure Container Apps Sandboxes provider.
/// </summary>
public static class AzureSandboxProviderRegistration
{
    /// <summary>
    /// Registers an <see cref="ISandboxProvider"/> named <c>azure</c> that runs sandboxes in an Azure
    /// Container Apps sandbox group.
    /// </summary>
    /// <param name="services">The services.</param>
    /// <param name="settings">The sandbox group and the deployment's scope.</param>
    /// <param name="credential">
    /// Signs the provider in, such as a managed identity when hosted or the Azure CLI's sign-in in
    /// development; it needs the <c>Container Apps SandboxGroup Data Owner</c> role on the group, and
    /// <c>AcrPull</c> in the settings' container registries.
    /// </param>
    public static IServiceCollection AddAzureSandboxProvider(this IServiceCollection services, AzureSandboxSettings settings, TokenCredential credential)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(credential);
        SandboxGroupClient client = new SandboxGroupClient(settings.Endpoint, settings.Group, credential);
        services.AddSingleton(_ => new ContainerRegistries(credential, settings.ContainerRegistries));
        services.AddSingleton<ISandboxProvider>(provider =>
            new AzureSandboxProvider(client, provider.GetRequiredService<ContainerRegistries>(), settings.Scope, TimeProvider.System));
        return services;
    }
}
