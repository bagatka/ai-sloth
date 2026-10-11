using System;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// Registers <see cref="ChatGptSignInClient"/> and <see cref="OpenAIModelsClient"/>.
/// </summary>
public static class ChatGptSignInRegistration
{
    /// <summary>
    /// Registers one <see cref="ChatGptSignInClient"/> for the whole application, disposed with the
    /// service provider. Call it once.
    /// </summary>
    public static IServiceCollection AddChatGptSignInClient(this IServiceCollection services, ChatGptSignInSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        services.AddSingleton(_ => new ChatGptSignInClient(settings));
        return services;
    }

    /// <summary>
    /// Registers one <see cref="CodexSignInClient"/> for OpenAI's authorization server at
    /// <paramref name="issuer"/>, disposed with the service provider. Call it once; the host registers a
    /// <see cref="TimeProvider"/>.
    /// </summary>
    public static IServiceCollection AddCodexSignInClient(this IServiceCollection services, Uri issuer)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(provider => new CodexSignInClient(issuer, provider.GetRequiredService<TimeProvider>()));
        return services;
    }

    /// <summary>
    /// Registers one <see cref="ChatGptUsageClient"/> for the whole application, disposed with the
    /// service provider. Call it once.
    /// </summary>
    public static IServiceCollection AddChatGptUsageClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(_ => new ChatGptUsageClient());
        return services;
    }

    /// <summary>
    /// Registers one <see cref="OpenAIModelsClient"/> for the whole application, disposed with the
    /// service provider. Call it once.
    /// </summary>
    public static IServiceCollection AddOpenAIModelsClient(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddSingleton(_ => new OpenAIModelsClient());
        return services;
    }
}
