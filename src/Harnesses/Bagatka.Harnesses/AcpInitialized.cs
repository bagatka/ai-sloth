namespace Bagatka.Harnesses;

/// <summary>The agent accepted <see cref="Acp.Initialize"/>; a session comes next (<see cref="Acp.NewSession"/>).</summary>
/// <param name="SupportsSteering">Whether the agent accepts messages into a running turn (<see cref="Acp.Steer"/>).</param>
public sealed record AcpInitialized(bool SupportsSteering);
