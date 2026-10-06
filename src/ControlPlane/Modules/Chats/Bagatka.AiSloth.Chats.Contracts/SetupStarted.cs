using System.Collections.Generic;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// The setup of the chat's nook started, as it does whenever the nook gets its files; the agent starts
/// once it ended.
/// </summary>
/// <param name="Scripts">The scripts it runs, as paths in the nook's files, in order.</param>
/// <param name="FromReadyCopy">
/// Whether it sets the nook up from a ready copy, a copy of a nook with the same files right after
/// its setup, so it has little left to do.
/// </param>
public sealed record SetupStarted(IReadOnlyList<string> Scripts, bool FromReadyCopy);
