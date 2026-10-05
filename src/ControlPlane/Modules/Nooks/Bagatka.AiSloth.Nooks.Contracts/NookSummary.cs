using System;
using System.Collections.Generic;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A nook as the people with access to it see it.
/// </summary>
/// <param name="Id">The nook.</param>
/// <param name="WorkspaceId">The workspace that owns it.</param>
/// <param name="Provider">The ID of the provider it runs on, as in <see cref="ProviderSummary.Id"/>.</param>
/// <param name="Status">Where it is in its lifecycle.</param>
/// <param name="CreatedAt">When it was recorded.</param>
/// <param name="Disk">How full its disk was at the daemon's last report, or <see langword="null"/> before the first.</param>
/// <param name="Harness">The harness it carries for its chat's agent, or <see langword="null"/> for none.</param>
/// <param name="Sources">Its sources, each a repository at <c>/work/&lt;name&gt;</c>, by name.</param>
public sealed record NookSummary(
    NookId Id,
    WorkspaceId WorkspaceId,
    string Provider,
    NookStatus Status,
    DateTimeOffset CreatedAt,
    DiskUsage? Disk,
    string? Harness,
    IReadOnlyList<NookSource> Sources);
