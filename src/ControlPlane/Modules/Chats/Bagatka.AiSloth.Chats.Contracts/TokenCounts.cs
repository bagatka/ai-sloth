namespace Bagatka.AiSloth.Chats.Contracts;

/// <summary>Tokens of model calls, added up; input counted once, as in <see cref="ModelCallUsage"/>.</summary>
/// <param name="Input">Input tokens neither read from a cache nor written to one.</param>
/// <param name="CacheRead">Input tokens read from a cache.</param>
/// <param name="CacheWrite">Input tokens written to a cache, for minutes or an hour.</param>
/// <param name="Output">Output tokens, reasoning included.</param>
/// <param name="Reasoning">Of the output, the reasoning tokens.</param>
public sealed record TokenCounts(long Input, long CacheRead, long CacheWrite, long Output, long Reasoning);
