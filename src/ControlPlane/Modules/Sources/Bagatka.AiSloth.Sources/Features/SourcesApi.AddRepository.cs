using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Sources.Data;
using Bagatka.AiSloth.Sources.Model;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sdk.GitHub;

namespace Bagatka.AiSloth.Sources;

internal sealed partial class SourcesApi
{
    public async Task<Result<RepositorySummary>> AddRepositoryAsync(Actor actor, AddRepository command, CancellationToken ct)
    {
        await using SourcesDbContext db = await databases.CreateDbContextAsync(ct);

        AccessLevel? access = await workspaces.GetAccessAsync(actor, Resource.Workspace(command.WorkspaceId), ct);
        if (access is null)
        {
            return new Result<RepositorySummary>(WorkspacesErrors.NotFound);
        }

        if (actor is not UserActor user || access < AccessLevel.Manage)
        {
            return new Result<RepositorySummary>(Error.Forbidden);
        }

        string[] parts = (command.FullName ?? string.Empty).Trim().Split('/');
        if (parts is not [{ Length: > 0 } owner, { Length: > 0 } name])
        {
            return new Result<RepositorySummary>(Error.Validation("fullName", "Must be owner/name, such as acme/api."));
        }

        Result<Connected> connected = await ConnectedAsync(db, actor, ct);
        if (connected.Failed)
        {
            return new Result<RepositorySummary>(connected.Error);
        }

        GitHubRepository? found;
        try
        {
            found = await github.GetRepositoryAsync(connected.Output.Token, owner, name, ct);
        }
        catch (HttpRequestException exception) when (Revoked(exception))
        {
            return new Result<RepositorySummary>(SourcesErrors.GitHubNotConnected);
        }

        if (found is null)
        {
            return new Result<RepositorySummary>(SourcesErrors.RepositoryUnreachable);
        }

        Repository repository = Repository.Add(command.WorkspaceId, found, user.UserId, time);
        db.Repositories.Add(repository);
        Result saved = await db.SaveAsync(ct);
        if (saved.Failed)
        {
            // The workspace has it, or another repository by its name, which is a folder in its nooks.
            // Not handled: two repositories of one name from different owners in one workspace.
            return new Result<RepositorySummary>(saved.Error == ModuleDbContextExtensions.AlreadyExists ? SourcesErrors.RepositoryAlreadyAdded : saved.Error);
        }

        productEvents.Capture(new ProductEvent("repository_added", user.UserId, command.WorkspaceId.Value, new Dictionary<string, ProductFact>(StringComparer.Ordinal)));
        return new Result<RepositorySummary>(repository.ToSummary());
    }
}
