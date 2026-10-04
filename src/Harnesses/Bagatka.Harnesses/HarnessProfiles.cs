using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.Harnesses;

/// <summary>
/// The harnesses this library knows. A host installs their programs where they run; the versions
/// it installs are the ones these profiles are written for.
/// </summary>
public static class HarnessProfiles
{
    /// <summary>
    /// Claude Code through its ACP adapter (<c>@agentclientprotocol/claude-agent-acp</c>). An API key
    /// stays behind a model gateway; a Claude subscription's token goes to the harness.
    /// </summary>
    public static HarnessProfile ClaudeCode { get; } = new HarnessProfile(
        "claude-code",
        "Claude Code",
        "claude-agent-acp",
        [],
        [
            new CredentialUse(CredentialKind.AnthropicApiKey, "ANTHROPIC_AUTH_TOKEN", GatewayVariable: "ANTHROPIC_BASE_URL"),
            new CredentialUse(CredentialKind.ClaudeOAuthToken, "CLAUDE_CODE_OAUTH_TOKEN", GatewayVariable: null),
        ],
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            // No auto-updates, telemetry, or error reports from where agents run.
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
        });

    /// <summary>GitHub Copilot CLI in ACP mode (<c>@github/copilot</c>), drawing on the token owner's Copilot plan.</summary>
    public static HarnessProfile Copilot { get; } = new HarnessProfile(
        "copilot",
        "GitHub Copilot",
        "copilot",
        ["--acp"],
        [new CredentialUse(CredentialKind.GitHubToken, "COPILOT_GITHUB_TOKEN", GatewayVariable: null)],
        new Dictionary<string, string>(StringComparer.Ordinal));

    /// <summary>Every harness, in the order people see them.</summary>
    public static IReadOnlyList<HarnessProfile> All { get; } = [ClaudeCode, Copilot];

    /// <summary>The harness with this ID, or <see langword="null"/>.</summary>
    public static HarnessProfile? Find(string id)
    {
        return All.SingleOrDefault(harness => string.Equals(harness.Id, id, StringComparison.Ordinal));
    }
}
