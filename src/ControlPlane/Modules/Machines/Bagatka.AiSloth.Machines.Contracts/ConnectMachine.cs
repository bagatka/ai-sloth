namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// Input to <see cref="IMachineConnectionsApi.ConnectAsync"/>: who the machine says it is.
/// </summary>
/// <param name="MachineId">The machine.</param>
/// <param name="Token">Its secret, from registration.</param>
public sealed record ConnectMachine(MachineId MachineId, string Token);
