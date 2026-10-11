using System.Text;
using System;

namespace Bagatka.Sdk.GitHub;

/// <summary>
/// A user access token: a GitHub App's, which acts as the person within the repositories the app is
/// installed on, or an OAuth app's, within its scopes. Never log it; its text form leaves the tokens out.
/// </summary>
/// <param name="AccessToken">Calls GitHub as the person: <c>Authorization: Bearer</c>, and git's password over HTTPS.</param>
/// <param name="ExpiresIn">How long it works, or <see langword="null"/> when the app's tokens never expire.</param>
/// <param name="RefreshToken">Gets a new token set, replacing itself; <see langword="null"/> when tokens never expire.</param>
/// <param name="RefreshTokenExpiresIn">How long the refresh token works.</param>
public sealed record GitHubUserTokens(string AccessToken, TimeSpan? ExpiresIn, string? RefreshToken, TimeSpan? RefreshTokenExpiresIn)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ExpiresIn = ").Append(ExpiresIn).Append(", AccessToken = ***, RefreshToken = ***");
        return true;
    }
}
