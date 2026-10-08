using System.Collections.Generic;
using Bagatka.AiSloth.Workspaces.Contracts;

using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INooksApi.CreateAsync"/>.
/// </summary>
/// <param name="WorkspaceId">The workspace that will own the nook.</param>
/// <param name="Provider">
/// The ID of the provider to run it on, one of <see cref="INooksApi.ListProvidersAsync"/>, such as
/// <c>docker</c> or <c>machine:0199b3a4-2f0c-7c4e-9a51-3d2f8e6b1c07</c>.
/// </param>
/// <param name="Image">
/// The image the nook starts from, by the name the deployment offers it under, such as
/// <c>claude-code</c> for the base image with that harness installed, or <see langword="null"/> for the
/// base image. It can't change later.
/// </param>
/// <param name="Repositories">
/// The workspace's repositories the nook starts with, each at <c>/work/&lt;name&gt;</c>, copied in with
/// the creator's GitHub connection before anything runs in the nook. Empty for an empty <c>/work</c>.
/// </param>
/// <param name="CopyOf">
/// Another nook of the workspace whose files this one starts with, with its repositories; or
/// <see langword="null"/>. Not together with repositories.
/// </param>
/// <param name="Checkpoint">
/// With <paramref name="CopyOf"/>, which of its checkpoints this nook starts from; <see langword="null"/>
/// for its files as they are when they're copied in, through a new checkpoint of it.
/// </param>
/// <param name="KeptPaths">
/// Absolute paths outside <c>/work</c> the nook's checkpoints keep too, such as where its agent keeps
/// its sessions; at most 10. Empty for none.
/// </param>
/// <param name="FromScratch">
/// Whether the nook starts from its image even when a ready copy of a nook with the same files would
/// start it faster, such as to test that a setup works from scratch.
/// </param>
/// <param name="ReservedFor">
/// The one person who may operate the nook, besides the control plane: run, feed, and stop its
/// processes, copy files in and out, and run its setup. Whoever operates a nook can use whatever its
/// agent can, so a chat on a personal account reserves its nook for that account's owner.
/// <see langword="null"/> for everyone with Write on the workspace.
/// </param>
public sealed record CreateNook(
    WorkspaceId WorkspaceId,
    string Provider,
    string? Image,
    IReadOnlyList<NookRepository> Repositories,
    NookId? CopyOf,
    int? Checkpoint,
    IReadOnlyList<string> KeptPaths,
    bool FromScratch,
    UserId? ReservedFor);
