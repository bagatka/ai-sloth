using System.Text.Json;

namespace Bagatka.Harnesses;

/// <summary>
/// A request the agent sent, such as <c>session/request_permission</c>; answer it with
/// <see cref="Acp.Allow"/> or <see cref="Acp.MethodNotFound"/>.
/// </summary>
/// <param name="Id">The agent's ID for it, echoed in the answer.</param>
/// <param name="Method">What it asks.</param>
/// <param name="Parameters">Its parameters.</param>
public sealed record AcpRequest(JsonElement Id, string Method, JsonElement Parameters)
{
    /// <summary>Whether it asks permission to run a tool.</summary>
    public bool IsPermissionRequest => string.Equals(Method, "session/request_permission", System.StringComparison.Ordinal);
}
