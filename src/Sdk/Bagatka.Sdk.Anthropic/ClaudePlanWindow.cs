using System;

namespace Bagatka.Sdk.Anthropic;

/// <summary>One of a Claude plan's usage windows.</summary>
/// <param name="Name">Which, as people read it: <c>5 hours</c>, <c>Week</c>, or <c>Week, Opus</c>.</param>
/// <param name="UsedPercent">How much of it is used, 0 to 100.</param>
/// <param name="ResetsAt">When it starts over, when Anthropic says.</param>
public sealed record ClaudePlanWindow(string Name, decimal UsedPercent, DateTimeOffset? ResetsAt);
