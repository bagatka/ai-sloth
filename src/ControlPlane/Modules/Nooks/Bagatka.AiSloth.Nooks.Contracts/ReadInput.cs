namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>Input to <see cref="INookDaemonsApi.ReadInputAsync"/>.</summary>
/// <param name="NookId">The nook the daemon runs in.</param>
/// <param name="Token">The nook's daemon token.</param>
/// <param name="ProcessId">The process started with <see cref="StartProcessInstruction.InputStreamed"/>.</param>
public sealed record ReadInput(NookId NookId, string Token, ProcessId ProcessId);
