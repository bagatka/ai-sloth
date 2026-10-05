using System.Collections.Generic;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// The project's setup started in the chat's nook, as it does whenever the nook gets its files; the
/// agent starts once it ended.
/// </summary>
/// <param name="Scripts">The scripts it runs, as paths relative to <c>/work</c>, in order.</param>
public sealed record SetupStarted(IReadOnlyList<string> Scripts);
