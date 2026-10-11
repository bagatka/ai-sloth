using System;

namespace Bagatka.Sdk.OpenAI;

/// <summary>One of a ChatGPT plan's usage windows.</summary>
/// <param name="UsedPercent">How much of it is used, 0 to 100.</param>
/// <param name="Minutes">How long it is, such as 300 for five hours or 10080 for a week.</param>
/// <param name="ResetsAt">When it starts over.</param>
public sealed record ChatGptWindow(decimal UsedPercent, int? Minutes, DateTimeOffset? ResetsAt);
