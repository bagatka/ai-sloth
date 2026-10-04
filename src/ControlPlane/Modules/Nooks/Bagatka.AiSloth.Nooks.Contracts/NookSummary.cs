using System;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A nook as its workspace members see it.
/// </summary>
/// <param name="Id">The nook.</param>
/// <param name="WorkspaceId">The workspace that owns it.</param>
/// <param name="Provider">The ID of the provider it runs on, as in <see cref="ProviderSummary.Id"/>.</param>
/// <param name="Status">Where it is in its lifecycle.</param>
/// <param name="CreatedAt">When it was recorded.</param>
/// <param name="Disk">How full its disk was at the daemon's last report, or <see langword="null"/> before the first.</param>
public sealed record NookSummary(
    NookId Id,
    WorkspaceId WorkspaceId,
    string Provider,
    NookStatus Status,
    DateTimeOffset CreatedAt,
    DiskUsage? Disk);
