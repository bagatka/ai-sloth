using System;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.OpenAI;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// A plan's sign-in, as its vendor's own agent signs in: Codex's for a ChatGPT plan, Claude Code's for a
// Claude plan. The client it signed in to, who signed in and to which account, and the current tokens.
// Kept sealed as the account's secret. Access tokens last hours; one is renewed shortly before it
// expires, one renewal at a time, since a renewal replaces the refresh token.
internal sealed record PlanSession(
    string ClientId,
    string? Subject,
    string? Email,
    string AccessToken,
    string RefreshToken,
    string? IdToken,
    DateTimeOffset ExpiresAt,
    DateTimeOffset RenewalDueAt,
    string? AccountId = null)
{
    private static readonly TimeSpan RenewalMargin = TimeSpan.FromMinutes(5);

    // Codex's sign-in, to the ChatGPT account its ID token names.
    public static PlanSession Start(ChatGptIdToken identity, ChatGptTokens tokens, DateTimeOffset now)
    {
        DateTimeOffset expiresAt = now + tokens.ExpiresIn;
        return new PlanSession(CodexSignInClient.ClientId, identity.Subject, identity.Email, tokens.AccessToken, tokens.RefreshToken, tokens.IdToken, expiresAt, expiresAt - RenewalMargin, identity.AccountId);
    }

    // Claude Code's sign-in.
    public static PlanSession Start(ClaudeTokens tokens, DateTimeOffset now)
    {
        DateTimeOffset expiresAt = now + tokens.ExpiresIn;
        return new PlanSession(ClaudeSignInClient.ClientId, tokens.AccountId, tokens.Email, tokens.AccessToken, tokens.RefreshToken, IdToken: null, expiresAt, expiresAt - RenewalMargin, tokens.AccountId);
    }

    public bool RenewalDue(DateTimeOffset now)
    {
        return now >= RenewalDueAt;
    }

    // A renewal replaces the tokens; the ID token stays when none came.
    public PlanSession Renewed(string accessToken, string refreshToken, string? idToken, TimeSpan expiresIn, DateTimeOffset now)
    {
        DateTimeOffset expiresAt = now + expiresIn;
        return this with
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            IdToken = idToken ?? IdToken,
            ExpiresAt = expiresAt,
            RenewalDueAt = expiresAt - RenewalMargin,
        };
    }
}
