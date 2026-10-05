namespace Bagatka.Harnesses;

/// <summary>The agent accepted <see cref="Acp.Initialize"/>; a session comes next (<see cref="Acp.NewSession"/> or <see cref="Acp.LoadSession"/>).</summary>
/// <param name="SupportsSteering">Whether the agent accepts messages into a running turn (<see cref="Acp.Steer"/>).</param>
/// <param name="SupportsLoading">Whether the agent loads an earlier session (<see cref="Acp.LoadSession"/>).</param>
public sealed record AcpInitialized(bool SupportsSteering, bool SupportsLoading);
