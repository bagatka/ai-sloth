using System;
using System.Collections.Generic;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>What a plan has used of its limits, as its vendor last reported it.</summary>
/// <param name="Meters">
/// Each limit or count the vendor reports, in its order; empty for an account that isn't a plan, or a
/// plan whose vendor has reported none yet.
/// </param>
/// <param name="ReportedAt">
/// When the vendor reported them: now, for a plan asked when someone looks (Copilot, Claude); at its
/// latest call, for a plan whose calls report them (ChatGPT); null before any report.
/// </param>
public sealed record PlanUsage(IReadOnlyList<UsageMeter> Meters, DateTimeOffset? ReportedAt)
{
    /// <summary>Nothing reported.</summary>
    public static PlanUsage None { get; } = new PlanUsage([], ReportedAt: null);
}
