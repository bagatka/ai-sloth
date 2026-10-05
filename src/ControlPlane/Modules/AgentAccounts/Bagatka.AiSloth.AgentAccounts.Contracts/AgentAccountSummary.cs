using System;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.AgentAccounts.Contracts;

/// <summary>
/// An agent account as people see it; its secret is never shown. A workspace's account has a
/// <paramref name="WorkspaceId"/>; a person's own has an <paramref name="OwnerId"/>.
/// </summary>
/// <param name="Id">The account.</param>
/// <param name="Kind">What it is at its vendor.</param>
/// <param name="Name">Its name for people, such as <c>Team key</c>.</param>
/// <param name="WorkspaceId">The workspace whose people with Write use it, for a workspace's account.</param>
/// <param name="OwnerId">The person it belongs to, for a personal account.</param>
/// <param name="AddedAt">When it was added.</param>
/// <param name="Endpoint">The API's base URL for an API key at another endpoint than the vendor's; otherwise <see langword="null"/>.</param>
/// <param name="NeedsSignIn">
/// Whether the account's sign-in ended, such as a ChatGPT plan whose owner disconnected the app in
/// ChatGPT: it runs no agents until it is removed and added again.
/// </param>
public sealed record AgentAccountSummary(
    AgentAccountId Id,
    AgentAccountKind Kind,
    string Name,
    WorkspaceId? WorkspaceId,
    UserId? OwnerId,
    DateTimeOffset AddedAt,
    Uri? Endpoint,
    bool NeedsSignIn);
