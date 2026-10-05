using System;
using System.Text;

namespace Bagatka.Sdk.OpenAI;

/// <summary>
/// A token set from OpenAI's token endpoint. Never log it; its text form leaves the tokens out.
/// </summary>
/// <param name="AccessToken">Calls the OpenAI API as the person, drawing on their plan: <c>Authorization: Bearer</c>.</param>
/// <param name="RefreshToken">Gets the next token set; every refresh replaces it, and a used one stops working.</param>
/// <param name="IdToken">Who signed in (<see cref="ChatGptSignInClient.ReadIdToken"/>); absent from some refreshes.</param>
/// <param name="Scope">The scopes granted, separated by spaces.</param>
/// <param name="ExpiresIn">How long the access token is valid from now: an hour.</param>
/// <param name="EarliestRefreshAt">When OpenAI would like the next refresh, if it said.</param>
public sealed record ChatGptTokens(
    string AccessToken,
    string RefreshToken,
    string? IdToken,
    string Scope,
    TimeSpan ExpiresIn,
    DateTimeOffset? EarliestRefreshAt)
{
    /// <summary>Whether the person allowed the app to use their plan (<see cref="ChatGptSignInClient.PlanScope"/>).</summary>
    public bool AllowsPlanUse => (" " + Scope + " ").Contains(" " + ChatGptSignInClient.PlanScope + " ", StringComparison.Ordinal);

    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Scope = ").Append(Scope).Append(", AccessToken = ***, RefreshToken = ***");
        return true;
    }
}
