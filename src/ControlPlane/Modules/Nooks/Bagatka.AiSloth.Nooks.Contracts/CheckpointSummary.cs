using System;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>A nook's files as they were at one moment, kept while the nook exists.</summary>
/// <param name="NookId">The nook.</param>
/// <param name="Number">Its number in the nook, counting from 1.</param>
/// <param name="CreatedAt">When it was taken.</param>
/// <param name="Note">What it follows, in words for people.</param>
public sealed record CheckpointSummary(NookId NookId, int Number, DateTimeOffset CreatedAt, string Note);
