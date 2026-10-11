using System;
using System.Collections.Generic;

namespace Bagatka.Sdk.GitHub;

/// <summary>
/// What GitHub Copilot's API needs to know who calls it and what pays: the person's GitHub token, the
/// integration asking, and the API version, as Copilot's CLI sends them. Copilot doesn't document its
/// API; these are what its CLI 1.0.91 sends, and what <see cref="CopilotModelsClient"/> and a gateway
/// forwarding a Copilot CLI's calls both use.
/// </summary>
public static class CopilotApi
{
    /// <summary>Copilot's API on github.com.</summary>
    public static Uri Public { get; } = new Uri("https://api.githubcopilot.com");

    /// <summary>
    /// The headers of a call that the plan of <paramref name="token"/>, a GitHub token, pays for. Never
    /// log them: they hold the token.
    /// </summary>
    public static IReadOnlyDictionary<string, string> Headers(string token)
    {
        ArgumentException.ThrowIfNullOrEmpty(token);
        return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["Authorization"] = "Bearer " + token,
            ["Copilot-Integration-Id"] = "copilot-developer-cli",
            ["X-GitHub-Api-Version"] = "2026-08-01",
        };
    }
}
