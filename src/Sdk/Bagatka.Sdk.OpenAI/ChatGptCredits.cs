namespace Bagatka.Sdk.OpenAI;

/// <summary>A ChatGPT plan's credits, which pay on once its windows are used up, when allowed.</summary>
/// <param name="HasCredits">Whether it has any.</param>
/// <param name="Unlimited">Whether they're unlimited.</param>
/// <param name="Balance">What's left, as OpenAI writes it.</param>
public sealed record ChatGptCredits(bool HasCredits, bool Unlimited, string? Balance);
