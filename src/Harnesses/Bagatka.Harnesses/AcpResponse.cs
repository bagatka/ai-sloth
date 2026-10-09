using System.Text.Json;

namespace Bagatka.Harnesses;

// The agent's answer to one of the client's requests: a result, or an error in words for people.
// Acp.Read matches it to its request and hands callers an AcpEvent instead.
internal sealed record AcpResponse(string Id, JsonElement Result, string? Error)
{
    // The new session's ID, from the answer to Acp.NewSession.
    public string SessionId => Result.GetProperty("sessionId").GetString()!;

    // The model the session uses, from the answer to Acp.NewSession or Acp.LoadSession, when the agent
    // says: the current value of its config option of the model category, such as "gpt-5-codex", or
    // "default" for the agent's own choice.
    public string? Model
    {
        get
        {
            JsonElement? options = Json.Property(Result, "configOptions");
            if (options?.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            foreach (JsonElement option in options.Value.EnumerateArray())
            {
                string? category = Json.Property(option, "category") is { ValueKind: JsonValueKind.String } named ? named.GetString() : null;
                JsonElement? current = string.Equals(category, "model", System.StringComparison.Ordinal) ? Json.Property(option, "currentValue") : null;
                if (current?.ValueKind == JsonValueKind.String)
                {
                    return current.Value.GetString();
                }
            }

            return null;
        }
    }

    // Why the turn ended, from the answer to Acp.Prompt, such as end_turn or cancelled.
    public string StopReason => Result.GetProperty("stopReason").GetString()!;

    // Whether the agent accepts messages into a running turn, from the answer to Acp.Initialize.
    public bool SupportsSteering
    {
        get
        {
            JsonElement? meta = Json.Property(Result, "_meta");
            JsonElement? steering = meta is null ? null : Json.Property(meta.Value, "steering");
            JsonElement? supported = steering is null ? null : Json.Property(steering.Value, "supported");
            return supported?.ValueKind == JsonValueKind.True;
        }
    }

    // Whether the agent loads earlier sessions, from the answer to Acp.Initialize.
    public bool SupportsLoading
    {
        get
        {
            JsonElement? capabilities = Json.Property(Result, "agentCapabilities");
            JsonElement? loading = capabilities is null ? null : Json.Property(capabilities.Value, "loadSession");
            return loading?.ValueKind == JsonValueKind.True;
        }
    }

    // Whether a steered message joined the running turn, from the answer to Acp.Steer.
    public bool Injected
    {
        get
        {
            string? outcome = Json.Property(Result, "outcome")?.GetString();
            return Error is null && string.Equals(outcome, "injected", System.StringComparison.Ordinal);
        }
    }
}
