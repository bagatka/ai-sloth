using System;
using System.Collections.Generic;
using Bagatka.AiSloth.Workspaces.Contracts;

using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// A nook as the people with access to it see it.
/// </summary>
/// <param name="Id">The nook.</param>
/// <param name="WorkspaceId">The workspace that owns it.</param>
/// <param name="Provider">The ID of the provider it runs on, as in <see cref="ProviderSummary.Id"/>.</param>
/// <param name="Status">Where it is in its lifecycle.</param>
/// <param name="CreatedAt">When it was recorded.</param>
/// <param name="Usage">What it uses of its disk, memory, and CPU while it runs; <see langword="null"/> while it doesn't.</param>
/// <param name="DiskNearlyFull">
/// Whether its disk is so full that its work, and keeping its files as checkpoints, may soon fail:
/// people should free space, or move to a nook with a bigger disk.
/// </param>
/// <param name="Image">The image it started from, such as one with a chat's harness, or <see langword="null"/> for the base image.</param>
/// <param name="ReservedFor">
/// The one person who may change what runs in it or its files, such as the owner of the personal
/// account its chat's agent works on; <see langword="null"/> for everyone with Write on its workspace.
/// </param>
/// <param name="Sources">Its sources, each a repository at <c>/work/&lt;name&gt;</c>, by name.</param>
public sealed record NookSummary(
    NookId Id,
    WorkspaceId WorkspaceId,
    string Provider,
    NookStatus Status,
    DateTimeOffset CreatedAt,
    NookUsage? Usage,
    bool DiskNearlyFull,
    string? Image,
    UserId? ReservedFor,
    IReadOnlyList<NookSource> Sources);
