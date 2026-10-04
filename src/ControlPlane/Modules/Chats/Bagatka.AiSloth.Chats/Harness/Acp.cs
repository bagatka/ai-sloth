using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Bagatka.AiSloth.Chats.Contracts;

namespace Bagatka.AiSloth.Chats.Harness;

// The Agent Client Protocol messages chats send, one JSON-RPC message per line
// (https://agentclientprotocol.com/protocol/overview). Request IDs say what they ask, so a response
// read after a restart still finds its purpose.
internal static class Acp
{
    public const string InitializeId = "initialize";
    public const string NewSessionId = "session/new";
    private const string PromptPrefix = "prompt:";
    private const string SteerPrefix = "steer:";

    public static string Initialize()
    {
        // No file system or terminal methods: the agent uses its own tools inside the nook.
        return Request(InitializeId, "initialize", new JsonObject
        {
            ["protocolVersion"] = 1,
            ["clientCapabilities"] = new JsonObject
            {
                ["fs"] = new JsonObject { ["readTextFile"] = false, ["writeTextFile"] = false },
                ["terminal"] = false,
            },
        });
    }

    public static string NewSession()
    {
        return Request(NewSessionId, "session/new", new JsonObject
        {
            ["cwd"] = ClaudeCodeHarness.WorkingDirectory,
            ["mcpServers"] = new JsonArray(),
        });
    }

    public static string Prompt(string sessionId, MessageId message, string text)
    {
        return Request(PromptPrefix + Format(message), "session/prompt", new JsonObject
        {
            ["sessionId"] = sessionId,
            ["prompt"] = Text(text),
        });
    }

    // The steering extension of Claude's and Codex's adapters: the message joins the running turn,
    // or the agent answers promptRequired when no turn runs.
    public static string Steer(string sessionId, MessageId message, string text)
    {
        return Request(SteerPrefix + Format(message), "_session/steering", new JsonObject
        {
            ["sessionId"] = sessionId,
            ["prompt"] = Text(text),
            ["_meta"] = new JsonObject { ["steering"] = new JsonObject { ["idleBehavior"] = "promptRequired" } },
        });
    }

    public static string Cancel(string sessionId)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "session/cancel",
            ["params"] = new JsonObject { ["sessionId"] = sessionId },
        }.ToJsonString();
    }

    // Agents act without asking inside their nook: every permission request gets its broadest allow.
    public static string Allow(JsonElement id, JsonElement options)
    {
        JsonElement[] offered = [.. options.EnumerateArray()];
        JsonElement chosen = offered.FirstOrDefault(option => string.Equals(Kind(option), "allow_always", StringComparison.Ordinal));
        if (chosen.ValueKind == JsonValueKind.Undefined)
        {
            chosen = offered.FirstOrDefault(option => string.Equals(Kind(option), "allow_once", StringComparison.Ordinal));
        }

        JsonObject outcome = chosen.ValueKind == JsonValueKind.Undefined
            ? new JsonObject { ["outcome"] = "cancelled" }
            : new JsonObject { ["outcome"] = "selected", ["optionId"] = chosen.GetProperty("optionId").GetString() };
        return Response(id, new JsonObject { ["outcome"] = outcome });
    }

    public static string MethodNotFound(JsonElement id)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = JsonNode.Parse(id.GetRawText()),
            ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "This client doesn't offer the method." },
        }.ToJsonString();
    }

    public static MessageId? PromptMessage(string id)
    {
        return MessageOf(id, PromptPrefix);
    }

    public static MessageId? SteeredMessage(string id)
    {
        return MessageOf(id, SteerPrefix);
    }

    private static string Request(string id, string method, JsonObject parameters)
    {
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString();
    }

    private static string Response(JsonElement id, JsonObject result)
    {
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = JsonNode.Parse(id.GetRawText()), ["result"] = result }.ToJsonString();
    }

    private static JsonArray Text(string text)
    {
        return [new JsonObject { ["type"] = "text", ["text"] = text }];
    }

    private static string? Kind(JsonElement option)
    {
        return option.TryGetProperty("kind", out JsonElement kind) ? kind.GetString() : null;
    }

    private static string Format(MessageId message)
    {
        return message.Value.ToString("D", CultureInfo.InvariantCulture);
    }

    private static MessageId? MessageOf(string id, string prefix)
    {
        return id.StartsWith(prefix, StringComparison.Ordinal) && Guid.TryParseExact(id[prefix.Length..], "D", out Guid value)
            ? MessageId.From(value)
            : null;
    }
}
