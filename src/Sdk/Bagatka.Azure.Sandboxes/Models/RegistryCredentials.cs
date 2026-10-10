using System;

namespace Bagatka.Azure.Sandboxes.Models;

/// <summary>
/// What the service signs in to a private container registry with to pull an image: a username and
/// a token, such as a registry token's, or an Azure Container Registry's refresh token with the
/// username <c>00000000-0000-0000-0000-000000000000</c>. The token never shows in
/// <see cref="ToString"/>.
/// </summary>
public sealed class RegistryCredentials
{
    /// <summary>Creates the credentials.</summary>
    /// <param name="username">The username the registry takes.</param>
    /// <param name="token">The token or password.</param>
    public RegistryCredentials(string username, string token)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        ArgumentException.ThrowIfNullOrWhiteSpace(token);
        Username = username;
        Token = token;
    }

    /// <summary>The username.</summary>
    public string Username { get; }

    /// <summary>The token or password.</summary>
    public string Token { get; }

    /// <summary>The username, without the token.</summary>
    public override string ToString()
    {
        return Username;
    }
}
