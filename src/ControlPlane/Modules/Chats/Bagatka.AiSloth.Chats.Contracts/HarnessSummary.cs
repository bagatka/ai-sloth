using System.Collections.Generic;
using Bagatka.AiSloth.AgentAccounts.Contracts;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// A harness chats can run, and the agent accounts it takes.
/// </summary>
/// <param name="Id">What a nook takes as its harness, such as <c>claude-code</c>.</param>
/// <param name="Name">Its name for people.</param>
/// <param name="Accepts">The kinds of agent account it can run on.</param>
public sealed record HarnessSummary(string Id, string Name, IReadOnlyList<AgentAccountKind> Accepts);
