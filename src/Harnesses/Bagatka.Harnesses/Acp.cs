using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Bagatka.Harnesses;

/// <summary>
/// The client's side of the Agent Client Protocol (https://agentclientprotocol.com): the messages a
/// client sends, one JSON-RPC message per line, and <see cref="Read"/> for the lines a harness writes.
/// A prompt or steer carries the caller's key, and the answer comes back with it, even when it is read
/// from saved output after a restart.
/// </summary>
public static class Acp
{
    /// <summary>The first request: negotiates the protocol. This client offers no file system or terminal methods, so agents use their own tools.</summary>
    public static string Initialize()
    {
        return Request(RequestIds.Initialize, "initialize", new JsonObject
        {
            ["protocolVersion"] = 1,
            ["clientCapabilities"] = new JsonObject
            {
                ["fs"] = new JsonObject { ["readTextFile"] = false, ["writeTextFile"] = false },
                ["terminal"] = false,
            },
        });
    }

    /// <summary>Starts a session working in <paramref name="workingDirectory"/>, an absolute path.</summary>
    public static string NewSession(string workingDirectory)
    {
        return Request(RequestIds.NewSession, "session/new", new JsonObject { ["cwd"] = workingDirectory, ["mcpServers"] = new JsonArray() });
    }

    /// <summary>Sends a message, starting a turn; its answer, <see cref="AcpPromptEnded"/> or <see cref="AcpPromptFailed"/>, carries <paramref name="key"/>.</summary>
    public static string Prompt(Guid key, string sessionId, string text)
    {
        return Request(RequestIds.Prompt(key), "session/prompt", new JsonObject { ["sessionId"] = sessionId, ["prompt"] = Text(text) });
    }

    /// <summary>
    /// Offers a message to the running turn, through the steering extension of Claude's and Codex's
    /// adapters (<see cref="AcpInitialized.SupportsSteering"/>). Its answer, <see cref="AcpSteerAnswered"/>
    /// with <paramref name="key"/>, says whether it joined the turn, or that no turn runs and it needs a prompt.
    /// </summary>
    public static string Steer(Guid key, string sessionId, string text)
    {
        return Request(RequestIds.Steer(key), "_session/steering", new JsonObject
        {
            ["sessionId"] = sessionId,
            ["prompt"] = Text(text),
            ["_meta"] = new JsonObject { ["steering"] = new JsonObject { ["idleBehavior"] = "promptRequired" } },
        });
    }

    /// <summary>Asks the agent to end the running turn; its prompt then answers <c>cancelled</c>.</summary>
    public static string Cancel(string sessionId)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["method"] = "session/cancel",
            ["params"] = new JsonObject { ["sessionId"] = sessionId },
        }.ToJsonString();
    }

    /// <summary>Answers a permission request with its broadest allow: always, else once, else cancelled.</summary>
    public static string Allow(AcpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        JsonElement? options = Json.Property(request.Parameters, "options");
        JsonElement[] offered = options?.ValueKind == JsonValueKind.Array ? [.. options.Value.EnumerateArray()] : [];
        JsonElement chosen = offered.FirstOrDefault(option => string.Equals(Kind(option), "allow_always", StringComparison.Ordinal));
        if (chosen.ValueKind == JsonValueKind.Undefined)
        {
            chosen = offered.FirstOrDefault(option => string.Equals(Kind(option), "allow_once", StringComparison.Ordinal));
        }

        JsonObject outcome = chosen.ValueKind == JsonValueKind.Undefined
            ? new JsonObject { ["outcome"] = "cancelled" }
            : new JsonObject { ["outcome"] = "selected", ["optionId"] = chosen.GetProperty("optionId").GetString() };
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = Id(request.Id), ["result"] = new JsonObject { ["outcome"] = outcome } }.ToJsonString();
    }

    /// <summary>Answers a request this client doesn't offer.</summary>
    public static string MethodNotFound(AcpRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = Id(request.Id),
            ["error"] = new JsonObject { ["code"] = -32601, ["message"] = "This client doesn't offer the method." },
        }.ToJsonString();
    }

    /// <summary>
    /// What a line a harness wrote means, or <see langword="null"/> when it is no protocol message or
    /// answers nothing this client asks: harnesses log to standard error, so anything else on standard
    /// output is noise.
    /// </summary>
    public static AcpEvent? Read(string line)
    {
        JsonElement root;
        try
        {
            root = JsonElement.Parse(line);
        }
        catch (JsonException)
        {
            return null;
        }

        JsonElement? method = Json.Property(root, "method");
        JsonElement? id = Json.Property(root, "id");
        JsonElement? parameters = Json.Property(root, "params");
        bool isCall = method?.ValueKind == JsonValueKind.String;
        if (isCall && id is not null)
        {
            return new AcpEvent(new AcpRequest(id.Value, method!.Value.GetString()!, parameters ?? default));
        }

        bool isUpdate = isCall && string.Equals(method!.Value.GetString(), "session/update", StringComparison.Ordinal);
        JsonElement? update = parameters is null ? null : Json.Property(parameters.Value, "update");
        if (isUpdate && update is not null)
        {
            return new AcpEvent(new AcpUpdate(update.Value));
        }

        bool isResponse = !isCall && id?.ValueKind == JsonValueKind.String;
        if (isResponse)
        {
            JsonElement? failure = Json.Property(root, "error");
            string? error = failure is null ? null : ErrorText(failure.Value);
            AcpResponse response = new AcpResponse(id!.Value.GetString()!, Json.Property(root, "result") ?? default, error);
            return Answer(response);
        }

        return null;
    }

    // What a response answers, found by the ID this client gave its request.
    private static AcpEvent? Answer(AcpResponse response)
    {
        bool initialized = string.Equals(response.Id, RequestIds.Initialize, StringComparison.Ordinal);
        bool sessionCreated = string.Equals(response.Id, RequestIds.NewSession, StringComparison.Ordinal);
        if (initialized || sessionCreated)
        {
            if (response.Error is not null)
            {
                return new AcpEvent(new AcpStartFailed(response.Error));
            }

            if (initialized)
            {
                return new AcpEvent(new AcpInitialized(response.SupportsSteering));
            }

            return new AcpEvent(new AcpSessionCreated(response.SessionId));
        }

        Guid? prompt = RequestIds.Prompted(response.Id);
        if (prompt is not null)
        {
            if (response.Error is not null)
            {
                return new AcpEvent(new AcpPromptFailed(prompt.Value, response.Error));
            }

            return new AcpEvent(new AcpPromptEnded(prompt.Value, response.StopReason));
        }

        Guid? steered = RequestIds.Steered(response.Id);
        if (steered is not null)
        {
            return new AcpEvent(new AcpSteerAnswered(steered.Value, response.Injected));
        }

        return null;
    }

    private static string Request(string id, string method, JsonObject parameters)
    {
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters }.ToJsonString();
    }

    private static JsonArray Text(string text)
    {
        return [new JsonObject { ["type"] = "text", ["text"] = text }];
    }

    private static JsonNode? Id(JsonElement id)
    {
        return JsonNode.Parse(id.GetRawText());
    }

    private static string? Kind(JsonElement option)
    {
        return Json.Property(option, "kind")?.GetString();
    }

    private static string ErrorText(JsonElement error)
    {
        string? message = Json.Property(error, "message")?.GetString();
        return message ?? "The agent reported an error.";
    }
}
