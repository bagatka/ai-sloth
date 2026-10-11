using System;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Sdk.OpenRouter;

/// <summary>
/// Registers <see cref="OpenRouterClient"/>.
/// </summary>
public static class OpenRouterClientRegistration
{
    /// <summary>
    /// Registers one <see cref="OpenRouterClient"/> for the whole application, disposed with the service
    /// provider. Call it once.
    /// </summary>
    public static IServiceCollection AddOpenRouterClient(this IServiceCollection services, OpenRouterSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        services.AddSingleton(_ => new OpenRouterClient(settings));
        return services;
    }
}
