using System;
using System.Text;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// A token set from OpenAI's token endpoint, for Codex's sign-in (<see cref="CodexSignInClient"/>). Never log it; its text form leaves the tokens out.
/// </summary>
/// <param name="AccessToken">Calls ChatGPT's Codex backend as the person, drawing on their plan: <c>Authorization: Bearer</c>.</param>
/// <param name="RefreshToken">Gets the next token set; every refresh replaces it, and a used one stops working.</param>
/// <param name="ExpiresIn">How long the access token is valid from now.</param>
/// <param name="AccountId">
/// The ChatGPT account, personal or a workspace's, whose plan the sign-in uses, which calls name
/// (<c>ChatGPT-Account-Id</c>); absent from a refresh, which keeps it.
/// </param>
public sealed record ChatGptTokens(string AccessToken, string RefreshToken, TimeSpan ExpiresIn, string? AccountId)
{
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("ExpiresIn = ").Append(ExpiresIn).Append(", AccessToken = ***, RefreshToken = ***");
        return true;
    }
}
