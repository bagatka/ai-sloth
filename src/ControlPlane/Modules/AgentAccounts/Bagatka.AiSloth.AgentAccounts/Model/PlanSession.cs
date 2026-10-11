using System;
using Bagatka.Sdk.OpenAI;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// A ChatGPT plan's sign-in, with ChatGPT or as Codex: the client ID it signed in to, who signed in and
// to which ChatGPT account, and the current tokens. Kept sealed as the account's secret. The access
// token lasts about an hour and is renewed when OpenAI asks, or shortly before it expires.
internal sealed record PlanSession(
    string ClientId,
    string Subject,
    string? Email,
    string AccessToken,
    string RefreshToken,
    string? IdToken,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RenewalDueAt,
    string? AccountId = null)
{
    public static PlanSession Start(string clientId, ChatGptIdToken identity, ChatGptTokens tokens, DateTimeOffset now)
    {
        DateTimeOffset expiresAt = now + tokens.ExpiresIn;
        return new PlanSession(clientId, identity.Subject, identity.Email, tokens.AccessToken, tokens.RefreshToken, tokens.IdToken, expiresAt, DueAt(tokens, expiresAt), identity.AccountId);
    }

    public bool RenewalDue(DateTimeOffset now)
    {
        return now >= RenewalDueAt;
    }

    // A renewal replaces the tokens; an ID token is kept for signing in again later when none came.
    public PlanSession Renewed(ChatGptTokens tokens, DateTimeOffset now)
    {
        DateTimeOffset expiresAt = now + tokens.ExpiresIn;
        return this with
        {
            AccessToken = tokens.AccessToken,
            RefreshToken = tokens.RefreshToken,
            IdToken = tokens.IdToken ?? IdToken,
            ExpiresAt = expiresAt,
            RenewalDueAt = DueAt(tokens, expiresAt),
        };
    }

    private static DateTimeOffset DueAt(ChatGptTokens tokens, DateTimeOffset expiresAt)
    {
        DateTimeOffset requested = tokens.EarliestRefreshAt ?? expiresAt - TimeSpan.FromMinutes(5);
        DateTimeOffset latest = expiresAt - TimeSpan.FromMinutes(1);
        return requested < latest ? requested : latest;
    }
}
