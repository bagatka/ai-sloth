using System;
using System.Collections.Generic;

namespace Bagatka.AiSloth.Chats.Harness;

// The one harness chats run today: Claude Code through its Agent Client Protocol adapter, which the
// nook image installs (src/Daemon/Dockerfile). Its model calls go to the control plane's model
// gateway with the chat's token, so no model credential ever enters a nook.
internal static class ClaudeCodeHarness
{
    public const string Command = "claude-agent-acp";

    public const string WorkingDirectory = "/work";

    public static Dictionary<string, string> Environment(Uri modelGateway, string token)
    {
        return new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["ANTHROPIC_BASE_URL"] = modelGateway.AbsoluteUri.TrimEnd('/'),
            ["ANTHROPIC_AUTH_TOKEN"] = token,

            // No auto-updates, telemetry, or error reports from nooks.
            ["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1",
        };
    }
}
