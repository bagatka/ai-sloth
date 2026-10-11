using System;

namespace Bagatka.ModelAccess;

/// <summary>One of a plan's limits or counts: a window, a quota, or spend without a limit.</summary>
/// <param name="Name">What it counts, as people read it, such as <c>5 hours</c>, <c>Week</c>, <c>Week, Opus</c>, <c>Premium requests</c>, or <c>Credits</c>.</param>
/// <param name="Used">How much is used: a percent of a window, a count of a quota, the credits spent.</param>
/// <param name="Limit">The most it allows, in the same unit: 100 for a window; null for spend without a limit.</param>
/// <param name="Unit"><c>percent</c>, <c>requests</c>, or <c>credits</c>.</param>
/// <param name="ResetsAt">When it starts over, when the vendor says.</param>
public sealed record UsageMeter(string Name, decimal Used, decimal? Limit, string Unit, DateTimeOffset? ResetsAt);
