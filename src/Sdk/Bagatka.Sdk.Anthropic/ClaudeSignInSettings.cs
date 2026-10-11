using System;

namespace Bagatka.Sdk.Anthropic;

/// <summary>Where Claude Code's sign-in happens (<see cref="ClaudeSignInClient"/>).</summary>
/// <param name="AuthorizeUrl">The page where the person approves, at Claude.</param>
/// <param name="TokenUrl">Where a code becomes tokens, and tokens are renewed.</param>
/// <param name="RedirectUri">The page that shows the person the code to bring back.</param>
public sealed record ClaudeSignInSettings(Uri AuthorizeUrl, Uri TokenUrl, Uri RedirectUri)
{
    /// <summary>Anthropic's own, as Claude Code signs in to a Claude account.</summary>
    public static ClaudeSignInSettings Public { get; } = new ClaudeSignInSettings(
        new Uri("https://claude.com/cai/oauth/authorize"),
        new Uri("https://platform.claude.com/v1/oauth/token"),
        new Uri("https://platform.claude.com/oauth/code/callback"));
}
