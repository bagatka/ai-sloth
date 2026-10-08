namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// Input to <see cref="IMachineConnectionsApi.RegisterAsync"/>.
/// </summary>
/// <param name="Code">The one-time code a manager got when adding the machine.</param>
public sealed record RegisterMachine(string Code);
