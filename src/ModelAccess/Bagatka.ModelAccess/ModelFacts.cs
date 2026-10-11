using System;
using System.Collections.Generic;

namespace Bagatka.ModelAccess;

/// <summary>
/// What's known of a model at an account: from the account's vendor, where it says, and otherwise from
/// models.dev's open catalog. What isn't known is <see langword="null"/>, never a guess.
/// </summary>
/// <param name="Reasons">Whether it reasons (thinks before answering).</param>
/// <param name="Efforts">
/// The reasoning efforts it takes, in its vendor's words, such as <c>low</c> and <c>high</c>; empty when
/// it takes a thinking budget or a switch instead, or doesn't reason.
/// </param>
/// <param name="ContextWindow">The most tokens its context holds.</param>
/// <param name="MaxOutput">The most tokens it writes in one answer.</param>
/// <param name="Price">What its tokens cost at this account.</param>
/// <param name="CacheLifetime">
/// How long the vendor keeps a prompt's cache after its last use, at least: Anthropic's five minutes,
/// OpenAI's five, or thirty for its newest models.
/// </param>
public sealed record ModelFacts(bool? Reasons, IReadOnlyList<string> Efforts, int? ContextWindow, int? MaxOutput, ModelPrice? Price, TimeSpan? CacheLifetime)
{
    /// <summary>Nothing known.</summary>
    public static ModelFacts Unknown { get; } = new ModelFacts(Reasons: null, [], ContextWindow: null, MaxOutput: null, Price: null, CacheLifetime: null);
}
