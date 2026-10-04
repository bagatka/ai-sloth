namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// Input to <see cref="IMachineConnectionsApi.RegisterAsync"/>.
/// </summary>
/// <param name="Code">The one-time code a manager got when adding the machine.</param>
/// <param name="Version">The version of sloth on the machine, for diagnostics.</param>
public sealed record RegisterMachine(string Code, string Version);
