using System.Text.Json;

namespace Bagatka.Harnesses;

// The agent's answer to one of the client's requests: a result, or an error in words for people.
// Acp.Read matches it to its request and hands callers an AcpEvent instead.
internal sealed record AcpResponse(string Id, JsonElement Result, string? Error)
{
    // The new session's ID, from the answer to Acp.NewSession.
    public string SessionId => Result.GetProperty("sessionId").GetString()!;

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
