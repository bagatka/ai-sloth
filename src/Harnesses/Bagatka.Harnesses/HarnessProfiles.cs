using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.Harnesses;

/// <summary>
/// The harnesses this library knows. A host's image for each installs its programs, at the versions
/// its start script (<c>src/Harnesses/start</c>) is written for, and that script as <see cref="HarnessProfile.Command"/>.
/// </summary>
public static class HarnessProfiles
{
    /// <summary>Claude Code through its ACP adapter, on Anthropic's API or a Claude subscription.</summary>
    public static HarnessProfile ClaudeCode { get; } = new HarnessProfile("claude-code", "Claude Code", [CredentialKind.AnthropicApi, CredentialKind.ClaudeOAuthToken]);

    /// <summary>Codex through its ACP adapter, on OpenAI's API.</summary>
    public static HarnessProfile Codex { get; } = new HarnessProfile("codex", "Codex", [CredentialKind.OpenAIApi]);

    /// <summary>pi through its ACP adapter, on OpenAI's or Anthropic's API.</summary>
    public static HarnessProfile Pi { get; } = new HarnessProfile("pi", "pi", [CredentialKind.OpenAIApi, CredentialKind.AnthropicApi]);

    /// <summary>GitHub Copilot CLI in ACP mode, drawing on the token owner's Copilot plan.</summary>
    public static HarnessProfile Copilot { get; } = new HarnessProfile("copilot", "GitHub Copilot", [CredentialKind.GitHubToken]);

    /// <summary>Every harness, in the order people see them.</summary>
    public static IReadOnlyList<HarnessProfile> All { get; } = [ClaudeCode, Codex, Pi, Copilot];

    /// <summary>The harness with this ID, or <see langword="null"/>.</summary>
    public static HarnessProfile? Find(string id)
    {
        return All.SingleOrDefault(harness => string.Equals(harness.Id, id, StringComparison.Ordinal));
    }
}
