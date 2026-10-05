using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.Harnesses;

/// <summary>
/// The harnesses this library knows. A host's image for each installs its programs, at the versions
/// its start script (<c>src/Harnesses/start</c>) is written for, and that script as <see cref="HarnessProfile.Command"/>.
/// Its paths are where those versions keep files when run as root, whose home is <c>/root</c>. Claude
/// Code's start script points its memory at <c>~/.claude/memory</c>, wherever it works.
/// </summary>
public static class HarnessProfiles
{
    /// <summary>Claude Code through its ACP adapter, on Anthropic's API or a Claude subscription.</summary>
    public static HarnessProfile ClaudeCode { get; } = new HarnessProfile(
        "claude-code",
        "Claude Code",
        [CredentialKind.AnthropicApi, CredentialKind.ClaudeOAuthToken],
        SessionPaths: ["/root/.claude/projects"],
        InstructionsPath: "/root/.claude/CLAUDE.md",
        StatePaths: ["/root/.claude/memory"]);

    /// <summary>Codex through its ACP adapter, on OpenAI's API.</summary>
    public static HarnessProfile Codex { get; } = new HarnessProfile(
        "codex",
        "Codex",
        [CredentialKind.OpenAIApi],
        SessionPaths: ["/root/.codex/sessions"],
        InstructionsPath: "/root/.codex/AGENTS.md",
        StatePaths: []);

    /// <summary>pi through its ACP adapter, on OpenAI's or Anthropic's API.</summary>
    public static HarnessProfile Pi { get; } = new HarnessProfile(
        "pi",
        "pi",
        [CredentialKind.OpenAIApi, CredentialKind.AnthropicApi],
        SessionPaths: ["/root/.pi/agent/sessions"],
        InstructionsPath: "/root/.pi/agent/AGENTS.md",
        StatePaths: []);

    /// <summary>GitHub Copilot CLI in ACP mode, drawing on the token owner's Copilot plan.</summary>
    public static HarnessProfile Copilot { get; } = new HarnessProfile(
        "copilot",
        "GitHub Copilot",
        [CredentialKind.GitHubToken],
        SessionPaths: ["/root/.copilot/session-state"],
        InstructionsPath: "/root/.copilot/copilot-instructions.md",
        StatePaths: []);

    /// <summary>Every harness, in the order people see them.</summary>
    public static IReadOnlyList<HarnessProfile> All { get; } = [ClaudeCode, Codex, Pi, Copilot];

    /// <summary>The harness with this ID, or <see langword="null"/>.</summary>
    public static HarnessProfile? Find(string id)
    {
        return All.SingleOrDefault(harness => string.Equals(harness.Id, id, StringComparison.Ordinal));
    }
}
