namespace Bagatka.Sdk.GitHub;

/// <summary>One of a Copilot plan's quotas: a count with an entitlement, or unlimited.</summary>
/// <param name="Id">Copilot's name for it, such as <c>premium_interactions</c>, <c>chat</c>, or <c>completions</c>.</param>
/// <param name="Entitlement">How many the plan allows this period.</param>
/// <param name="Remaining">How many are left.</param>
/// <param name="Unlimited">Whether it has no limit, as on an enterprise plan billed for what it uses.</param>
/// <param name="CreditsUsed">The credits spent this period, for a plan billed by tokens, when Copilot says.</param>
public sealed record CopilotQuotaSnapshot(string Id, decimal Entitlement, decimal Remaining, bool Unlimited, decimal? CreditsUsed);
