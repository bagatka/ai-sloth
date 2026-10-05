using System;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.Sdk.GitHub;

/// <summary>
/// Registers <see cref="GitHubClient"/>.
/// </summary>
public static class GitHubClientRegistration
{
    /// <summary>
    /// Registers one <see cref="GitHubClient"/> for the whole application, disposed with the service
    /// provider. Call it once.
    /// </summary>
    public static IServiceCollection AddGitHubClient(this IServiceCollection services, GitHubSettings settings)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(settings);
        services.AddSingleton(_ => new GitHubClient(settings));
        return services;
    }
}
