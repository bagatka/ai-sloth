namespace Bagatka.Azure.Sandboxes;

/// <summary>
/// The names this library reports under, for OpenTelemetry: add the activity source with
/// <c>AddSource(SandboxesDiagnostics.Name)</c> and the meter with <c>AddMeter(SandboxesDiagnostics.Name)</c>.
/// </summary>
/// <remarks>
/// Each client call is an activity named after its method, such as <c>SandboxGroupClient.CreateSandbox</c>,
/// with <c>az.namespace</c>, <c>server.address</c>, and on failure <c>error.type</c>; the HTTP requests
/// under it are Azure.Core's. The meter records <c>bagatka.azure.sandboxes.client.operation.duration</c>,
/// in seconds, by <c>operation</c> and <c>error.type</c>.
/// </remarks>
public static class SandboxesDiagnostics
{
    /// <summary>The activity source's and the meter's name.</summary>
    public const string Name = "Bagatka.Azure.Sandboxes";
}
