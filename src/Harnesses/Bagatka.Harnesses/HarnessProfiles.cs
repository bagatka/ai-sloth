using System;
using System.Collections.Generic;
using System.Linq;

namespace Bagatka.Harnesses;

/// <summary>
/// The harnesses this library knows. A host's image for each installs its programs, at the versions
/// its start script (<c>src/Harnesses/start</c>) is written for, and that script as <see cref="HarnessProfile.Command"/>.
/// Its paths are where those versions keep files when run as root, whose home is <c>/root</c>. Claude
/// Code's start script points its memory at <c>~/.claude/memory</c>, wherever it works. Codex, pi,
/// and Copilot write no memory of their own that we know of, so they keep no state. Copilot's
/// instructions file is confirmed by <c>copilot instruction list</c>, not by a test.
/// </summary>
public static class HarnessProfiles
{
    /// <summary>Claude Code through its ACP adapter, on Anthropic's API, OpenRouter, or a Claude subscription.</summary>
    public static HarnessProfile ClaudeCode { get; } = new HarnessProfile(
        "claude-code",
        "Claude Code",
        [CredentialKind.AnthropicApi, CredentialKind.OpenRouterApi, CredentialKind.ClaudeOAuthToken],
        SessionPaths: ["/root/.claude/projects"],
        InstructionsPath: "/root/.claude/CLAUDE.md",
        StatePaths: ["/root/.claude/memory"],
        ModelFamily: "claude",
        ThinkingLevels: ["low", "medium", "high", "xhigh", "max"],
        DefaultThinking: "high");

    /// <summary>Codex through its ACP adapter, on OpenAI's API.</summary>
    public static HarnessProfile Codex { get; } = new HarnessProfile(
        "codex",
        "Codex",
        [CredentialKind.OpenAIApi],
        SessionPaths: ["/root/.codex/sessions"],
        InstructionsPath: "/root/.codex/AGENTS.md",
        StatePaths: [],
        ModelFamily: "gpt",
        ThinkingLevels: ["low", "medium", "high", "xhigh"],
        DefaultThinking: "medium");

    /// <summary>pi through its ACP adapter, on OpenAI's or Anthropic's API, OpenRouter, or Copilot's.</summary>
    public static HarnessProfile Pi { get; } = new HarnessProfile(
        "pi",
        "pi",
        [CredentialKind.OpenAIApi, CredentialKind.AnthropicApi, CredentialKind.OpenRouterApi, CredentialKind.CopilotApi],
        SessionPaths: ["/root/.pi/agent/sessions"],
        InstructionsPath: "/root/.pi/agent/AGENTS.md",
        StatePaths: [],
        ModelFamily: null,
        ThinkingLevels: ["off", "minimal", "low", "medium", "high", "xhigh", "max"],
        DefaultThinking: "medium");

    /// <summary>GitHub Copilot CLI in ACP mode, on Copilot's API.</summary>
    public static HarnessProfile Copilot { get; } = new HarnessProfile(
        "copilot",
        "GitHub Copilot",
        [CredentialKind.CopilotApi],
        SessionPaths: ["/root/.copilot/session-state"],
        InstructionsPath: "/root/.copilot/copilot-instructions.md",
        StatePaths: [],
        ModelFamily: null,
        ThinkingLevels: ["none", "minimal", "low", "medium", "high", "xhigh", "max"],
        DefaultThinking: "medium");

    /// <summary>Every harness, in the order people see them.</summary>
    public static IReadOnlyList<HarnessProfile> All { get; } = [ClaudeCode, Codex, Pi, Copilot];

    /// <summary>The harness with this ID, or <see langword="null"/>.</summary>
    public static HarnessProfile? Find(string id)
    {
        return All.SingleOrDefault(harness => string.Equals(harness.Id, id, StringComparison.Ordinal));
    }
}
