using System.Collections.Generic;
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>
/// A harness chats can run, and the agent accounts it takes.
/// </summary>
/// <param name="Id">What a nook takes as its harness, such as <c>claude-code</c>.</param>
/// <param name="Name">Its name for people.</param>
/// <param name="Accepts">The kinds of agent account it can run on.</param>
/// <param name="ThinkingLevels">How hard it can think, in its own words, such as <c>high</c>, lightest first.</param>
/// <param name="DefaultThinking">The level, one of its levels, a new chat thinks at unless someone chooses another.</param>
public sealed record HarnessSummary(string Id, string Name, IReadOnlyList<ModelAccessKind> Accepts, IReadOnlyList<string> ThinkingLevels, string DefaultThinking);
