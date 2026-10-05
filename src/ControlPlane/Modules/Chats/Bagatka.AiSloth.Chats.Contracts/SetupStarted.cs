using System.Collections.Generic;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// The setup of the chat's nook started, as it does whenever the nook gets its files; the agent starts
/// once it ended.
/// </summary>
/// <param name="Scripts">The scripts it runs, as paths in the nook's files, in order.</param>
public sealed record SetupStarted(IReadOnlyList<string> Scripts);
