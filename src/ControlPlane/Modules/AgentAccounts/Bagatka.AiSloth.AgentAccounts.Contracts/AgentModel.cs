namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>A model an account's vendor offers, which a chat's agent can run on.</summary>
/// <param name="Id">Its ID at the vendor, such as <c>openai/gpt-6-luna</c> at OpenRouter.</param>
/// <param name="Name">Its name for people, as the vendor gives it, such as <c>OpenAI: GPT-6 Luna</c>.</param>
/// <param name="UsesTools">Whether it can call tools, which every coding agent needs.</param>
/// <param name="Facts">What's known of it at this account: how it reasons, its limits, and its price.</param>
public sealed record AgentModel(string Id, string Name, bool UsesTools, ModelFacts Facts);
