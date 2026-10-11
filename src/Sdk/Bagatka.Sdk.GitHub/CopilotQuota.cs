using System;
using System.Collections.Generic;

namespace Bagatka.Sdk.GitHub;

/// <summary>A Copilot plan's quotas as Copilot reports them for the person (<see cref="GitHubClient.GetCopilotQuotaAsync"/>).</summary>
/// <param name="Snapshots">Each quota, such as <c>premium_interactions</c> and <c>chat</c>.</param>
/// <param name="ResetsAt">When the quotas start over, when Copilot says.</param>
public sealed record CopilotQuota(IReadOnlyList<CopilotQuotaSnapshot> Snapshots, DateTimeOffset? ResetsAt);
