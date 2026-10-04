using System;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A nook as its workspace members see it.
/// </summary>
/// <param name="Id">The nook.</param>
/// <param name="WorkspaceId">The workspace that owns it.</param>
/// <param name="Provider">The sandbox provider it runs on.</param>
/// <param name="Status">Where it is in its lifecycle.</param>
/// <param name="CreatedAt">When it was recorded.</param>
public sealed record NookSummary(
    NookId Id,
    WorkspaceId WorkspaceId,
    string Provider,
    NookStatus Status,
    DateTimeOffset CreatedAt);
