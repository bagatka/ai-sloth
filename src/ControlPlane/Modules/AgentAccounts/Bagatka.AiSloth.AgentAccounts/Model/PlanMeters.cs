using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.Foundation;
using Bagatka.Sdk.Anthropic;
using Bagatka.Sdk.GitHub;
using Bagatka.Sdk.OpenAI;

namespace Bagatka.AiSloth.AgentAccounts.Model;

// A plan's limits as meters, in the terms its vendor counts in: ChatGPT's and Claude's windows as the
// percent used, Copilot's quotas as the requests used of those allowed, or, on a plan without limits,
// the credits spent.
internal static class PlanMeters
{
    private const string CodexHeaders = "x-codex-";

    // The headers of a ChatGPT plan's answer that report its limits, as AgentAccount.ReportedLimits
    // keeps them; null when the answer reports none.
    public static string? ChatGptReport(IReadOnlyDictionary<string, string> headers)
    {
        SortedDictionary<string, string> reported = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (KeyValuePair<string, string> header in headers)
        {
            if (header.Key.StartsWith(CodexHeaders, StringComparison.OrdinalIgnoreCase))
            {
                reported[header.Key] = header.Value;
            }
        }

        return reported.Count == 0 ? null : JsonSerializer.Serialize(reported, FoundationJson.Options);
    }

    // Not handled: a ChatGPT plan's credits, which it spends once a window is used up; its balance
    // isn't a part used of a whole, so it needs a meter of its own kind.
    public static IReadOnlyList<UsageMeter> OfChatGpt(string? report)
    {
        Dictionary<string, string>? headers = report is null ? null : JsonSerializer.Deserialize<Dictionary<string, string>>(report, FoundationJson.Options);
        ChatGptPlanLimits? limits = headers is null ? null : ChatGptPlanLimits.Read(headers);
        List<UsageMeter> meters = [];
        if (limits?.Primary is { } primary)
        {
            meters.Add(new UsageMeter(WindowName(primary.Minutes, "Short window"), primary.UsedPercent, 100, "percent", primary.ResetsAt));
        }

        if (limits?.Secondary is { } secondary)
        {
            meters.Add(new UsageMeter(WindowName(secondary.Minutes, "Long window"), secondary.UsedPercent, 100, "percent", secondary.ResetsAt));
        }

        return meters;
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
    private static string WindowName(int? minutes, string unknown)
    {
        return minutes switch
        {
            null or <= 0 => unknown,
            10_080 => "Week",
            1_440 => "Day",
            _ when minutes % 1_440 == 0 => string.Create(CultureInfo.InvariantCulture, $"{minutes / 1_440} days"),
            60 => "Hour",
            _ when minutes % 60 == 0 => string.Create(CultureInfo.InvariantCulture, $"{minutes / 60} hours"),
            _ => string.Create(CultureInfo.InvariantCulture, $"{minutes} minutes"),
        };
    }
}
