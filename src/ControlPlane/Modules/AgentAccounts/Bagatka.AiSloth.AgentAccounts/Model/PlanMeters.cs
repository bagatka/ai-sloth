using System;
using System.Collections.Generic;
using System.Linq;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.GitHub;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// A plan's limits as meters, in the terms its vendor counts in: Claude's windows as the
// percent used, Copilot's quotas as the requests used of those allowed, or, on a plan without limits,
// the credits spent.
internal static class PlanMeters
{
    // Premium requests and chat requests, each of those the plan allows; code completions aren't an
    // agent's. A plan without limits that bills what it uses reports the credits it spent instead.
    public static IReadOnlyList<UsageMeter> OfCopilot(CopilotQuota quota)
    {
        (string Id, string Name)[] counted = [("premium_interactions", "Premium requests"), ("chat", "Chat requests")];
        List<UsageMeter> meters = [];
        foreach ((string id, string name) in counted)
        {
            CopilotQuotaSnapshot? snapshot = quota.Snapshots.FirstOrDefault(found => string.Equals(found.Id, id, StringComparison.Ordinal));
            if (snapshot is { Unlimited: false, Entitlement: > 0 })
            {
                meters.Add(new UsageMeter(name, snapshot.Entitlement - snapshot.Remaining, snapshot.Entitlement, "requests", quota.ResetsAt));
            }
        }

        CopilotQuotaSnapshot? billed = quota.Snapshots.FirstOrDefault(found => found is { Unlimited: true, CreditsUsed: not null });
        if (billed is not null)
        {
            meters.Add(new UsageMeter("Credits", billed.CreditsUsed!.Value, Limit: null, "credits", quota.ResetsAt));
        }

        return meters;
    }

    public static IReadOnlyList<UsageMeter> OfClaude(IReadOnlyList<ClaudePlanWindow> windows)
    {
        return [.. windows.Select(window => new UsageMeter(window.Name, window.UsedPercent, 100, "percent", window.ResetsAt))];
    }

}
