using System;

namespace Bagatka.Sdk.OpenAI;

/// <summary>One of a ChatGPT plan's usage windows (<see cref="ChatGptUsageClient"/>), such as five hours or a week.</summary>
/// <param name="Length">How long the window is, when ChatGPT says.</param>
/// <param name="UsedPercent">How much of it is used, from 0 to 100.</param>
/// <param name="ResetsAt">When it starts over, when ChatGPT says.</param>
public sealed record ChatGptUsageWindow(TimeSpan? Length, decimal UsedPercent, DateTimeOffset? ResetsAt);
