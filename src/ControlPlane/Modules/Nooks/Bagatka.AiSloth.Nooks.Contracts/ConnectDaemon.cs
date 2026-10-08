namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INookDaemonsApi.ConnectAsync"/>: who the daemon says it is.
/// </summary>
/// <param name="NookId">The nook the daemon runs in.</param>
/// <param name="Token">The secret this module issued for the nook when it was created.</param>
public sealed record ConnectDaemon(NookId NookId, string Token);
