using System;

namespace Bagatka.Sdk.Docker;

/// <summary>
/// Where the Docker Engine listens.
/// </summary>
public sealed record DockerClientSettings
{
    /// <summary>Creates the settings.</summary>
    /// <param name="endpoint">
    /// The engine's Unix socket, such as <c>unix:///var/run/docker.sock</c>. Only Unix sockets are
    /// supported; on Windows, run inside WSL with Docker Desktop's WSL integration.
    /// </param>
    public DockerClientSettings(Uri endpoint)
    {
        ArgumentNullException.ThrowIfNull(endpoint);
        if (!string.Equals(endpoint.Scheme, "unix", StringComparison.Ordinal) || endpoint.AbsolutePath.Length <= 1)
        {
            throw new ArgumentException("The Docker endpoint must be a Unix socket, such as unix:///var/run/docker.sock.", nameof(endpoint));
        }

        Endpoint = endpoint;
    }

    /// <summary>The engine's Unix socket.</summary>
    public Uri Endpoint { get; }
}
