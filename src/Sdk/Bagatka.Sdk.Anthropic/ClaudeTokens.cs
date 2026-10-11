using System;
using System.Text;

namespace Bagatka.Sdk.Anthropic;

/// <summary>A Claude plan's tokens from Claude Code's sign-in (<see cref="ClaudeSignInClient"/>). Never log it; its text form leaves the tokens out.</summary>
/// <param name="AccessToken">Calls Anthropic's API on the plan, as a bearer token with the <c>oauth-2025-04-20</c> beta.</param>
/// <param name="RefreshToken">Gets the next tokens; every renewal replaces it.</param>
/// <param name="ExpiresIn">How long the access token works from now: eight hours.</param>
/// <param name="AccountId">The Claude account that signed in, when Anthropic says.</param>
/// <param name="Email">Its email address, when Anthropic says.</param>
public sealed record ClaudeTokens(string AccessToken, string RefreshToken, TimeSpan ExpiresIn, string? AccountId, string? Email)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ExpiresIn = ").Append(ExpiresIn).Append(", AccessToken = ***, RefreshToken = ***");
        return true;
    }
}
