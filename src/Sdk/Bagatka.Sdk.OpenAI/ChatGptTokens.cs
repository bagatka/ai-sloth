using System;
using System.Text;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// A token set from OpenAI's token endpoint, for Codex's sign-in (<see cref="CodexSignInClient"/>). Never log it; its text form leaves the tokens out.
/// </summary>
/// <param name="AccessToken">Calls the OpenAI API as the person, drawing on their plan: <c>Authorization: Bearer</c>.</param>
/// <param name="RefreshToken">Gets the next token set; every refresh replaces it, and a used one stops working.</param>
/// <param name="IdToken">Who signed in (<see cref="ChatGptIdToken.Read"/>); absent from refreshes.</param>
/// <param name="ExpiresIn">How long the access token is valid from now: about an hour.</param>
public sealed record ChatGptTokens(
    string AccessToken,
    string RefreshToken,
    string? IdToken,
    TimeSpan ExpiresIn)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ExpiresIn = ").Append(ExpiresIn).Append(", AccessToken = ***, RefreshToken = ***");
        return true;
    }
}
