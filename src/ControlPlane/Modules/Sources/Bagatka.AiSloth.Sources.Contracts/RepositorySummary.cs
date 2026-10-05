using System;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Sources.Contracts;

/// <summary>
/// A repository a workspace connected, which its nooks can start with at <c>/work/&lt;name&gt;</c>.
/// </summary>
/// <param name="Id">The repository.</param>
/// <param name="WorkspaceId">The workspace that connected it.</param>
/// <param name="Name">Its folder in a nook, the repository's own name, such as <c>api</c>.</param>
/// <param name="FullName">Its name on GitHub, such as <c>acme/api</c>.</param>
/// <param name="DefaultBranch">The branch nooks start from unless told otherwise, which AiSloth never pushes to.</param>
/// <param name="Url">Its page on GitHub.</param>
/// <param name="AddedBy">Who connected it.</param>
/// <param name="AddedAt">When.</param>
public sealed record RepositorySummary(
    RepositoryId Id,
    WorkspaceId WorkspaceId,
    string Name,
    string FullName,
    string DefaultBranch,
    Uri Url,
    UserId AddedBy,
    DateTimeOffset AddedAt);
