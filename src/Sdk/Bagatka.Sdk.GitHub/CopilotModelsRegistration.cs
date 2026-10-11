using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Sdk.GitHub;

/// <summary>Registers <see cref="CopilotModelsClient"/>.</summary>
public static class CopilotModelsRegistration
{
    /// <summary>Adds one <see cref="CopilotModelsClient"/> for the whole process.</summary>
    public static IServiceCollection AddCopilotModelsClient(this IServiceCollection services)
    {
        services.AddSingleton<CopilotModelsClient>();
        return services;
    }
}
