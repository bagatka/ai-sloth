namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INookDaemonsApi.AcceptOutputAsync"/>: which watch an upload serves.
/// </summary>
/// <param name="NookId">The nook the daemon runs in.</param>
/// <param name="Token">The nook's daemon token.</param>
/// <param name="WatchId">The watch from the <see cref="WatchOutputInstruction"/> being served.</param>
public sealed record OutputUpload(NookId NookId, string Token, WatchId WatchId);
