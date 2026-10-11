using System;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Sdk.Anthropic;

/// <summary>
/// Registers <see cref="AnthropicModelsClient"/>.
/// </summary>
public static class AnthropicModelsRegistration
{
    /// <summary>
    /// Registers one <see cref="AnthropicModelsClient"/> for the whole application, disposed with the
    /// service provider. Call it once.
    /// </summary>
    public static IServiceCollection AddAnthropicModelsClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(_ => new AnthropicModelsClient());
        return services;
    }
}
