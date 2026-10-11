using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.GitHub;
using Bagatka.Sdk.OpenAI;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// A plan's limits as meters, in the terms its vendor counts in: ChatGPT's and Claude's windows as the
// percent used, Copilot's quotas as the requests used of those allowed, or, on a plan without limits,
// the credits spent.
internal static class PlanMeters
{
    // ChatGPT's five-hour and weekly windows.
    public static IReadOnlyList<UsageMeter> OfChatGpt(IReadOnlyList<ChatGptUsageWindow> windows)
    {
        return [.. windows.Select(window => new UsageMeter(WindowName(window.Length), window.UsedPercent, 100, "percent", window.ResetsAt))];
    }

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

    // ChatGPT's windows by their length: five hours and a week on today's plans.
    private static string WindowName(TimeSpan? length)
    {
        int minutes = length is TimeSpan known ? (int)known.TotalMinutes : 0;
        return minutes switch
        {
            <= 0 => "Window",
            10_080 => "Week",
            1_440 => "Day",
            _ when minutes % 1_440 == 0 => string.Create(CultureInfo.InvariantCulture, $"{minutes / 1_440} days"),
            60 => "Hour",
            _ when minutes % 60 == 0 => string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} hours"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes"),
        };
    }
}
