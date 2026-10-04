using System;
using System.Buffers;
using Bagatka.Sdk.Docker;

namespace Bagatka.Sandboxing.Docker;

/// <summary>
/// Settings for <see cref="DockerSandboxProviderRegistration.AddDockerSandboxProvider"/>.
/// </summary>
public sealed record DockerSandboxSettings
{
    private const int MaxScopeLength = 40;

    private static readonly SearchValues<char> ScopeCharacters = SearchValues.Create("abcdefghijklmnopqrstuvwxyz0123456789-");

    /// <summary>Creates the settings.</summary>
    /// <param name="endpoint">The Docker Engine's Unix socket, such as <c>unix:///var/run/docker.sock</c>.</param>
    /// <param name="scope">
    /// The deployment this provider serves: 1 to 40 lowercase letters, digits, or hyphens. Several
    /// deployments can share one Docker Engine because each touches only its own scope.
    /// </param>
    public DockerSandboxSettings(Uri endpoint, string scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.Length is 0 or > MaxScopeLength || scope.AsSpan().ContainsAnyExcept(ScopeCharacters))
        {
            throw new ArgumentException("The scope must be 1 to 40 lowercase letters, digits, or hyphens.", nameof(scope));
        }

        Client = new DockerClientSettings(endpoint);
        Scope = scope;
    }

    /// <summary>Where the Docker Engine listens.</summary>
    public DockerClientSettings Client { get; }

    /// <summary>The Docker Engine's Unix socket; configuration binds it by this name.</summary>
    public Uri Endpoint => Client.Endpoint;

    /// <summary>The deployment this provider serves.</summary>
    public string Scope { get; }
}
