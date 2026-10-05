using System.Collections.Generic;
using Bagatka.AiSloth.Workspaces.Contracts;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Input to <see cref="INooksApi.CreateAsync"/>.
/// </summary>
/// <param name="WorkspaceId">The workspace that will own the nook.</param>
/// <param name="Provider">
/// The ID of the provider to run it on, one of <see cref="INooksApi.ListProvidersAsync"/>, such as
/// <c>docker</c> or <c>machine:0199b3a4-2f0c-7c4e-9a51-3d2f8e6b1c07</c>.
/// </param>
/// <param name="Harness">
/// The harness the nook carries for its chat's agent, such as <c>claude-code</c>, or
/// <see langword="null"/> for a nook without a chat. It can't change later.
/// </param>
/// <param name="Repositories">
/// The workspace's repositories the nook starts with, each at <c>/work/&lt;name&gt;</c>, copied in with
/// the creator's GitHub connection before anything runs in the nook. Empty for an empty <c>/work</c>.
/// </param>
/// <param name="CopyOf">
/// Another nook of the workspace whose <c>/work</c> this one starts with a copy of, as it is when the
/// copy is made, with its repositories; or <see langword="null"/>. Not together with repositories.
/// </param>
public sealed record CreateNook(WorkspaceId WorkspaceId, string Provider, string? Harness, IReadOnlyList<NookRepository> Repositories, NookId? CopyOf);
