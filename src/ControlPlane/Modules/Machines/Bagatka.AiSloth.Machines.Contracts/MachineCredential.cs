namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// What a machine keeps to connect: its ID and its secret token, which only this response ever shows.
/// </summary>
/// <param name="MachineId">The machine.</param>
/// <param name="Token">The machine's secret.</param>
public sealed record MachineCredential(MachineId MachineId, string Token);
