using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bagatka.AiSloth.WebApi.Endpoints;

internal static class SourcesEndpoints
{
    internal sealed record AddRepositoryRequest(string FullName);

    internal sealed record GitSettingsRequest(GitIdentity? Author, GitIdentity? Committer, bool AiSlothCoAuthor, string BranchPrefix);

    public static void MapSourcesEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder gitHub = app.MapGroup("/github").WithTags("Sources");
        gitHub.MapPost("/connections", StartConnection);
        gitHub.MapPost("/connections/{id:guid}/complete", CompleteConnection);
        gitHub.MapGet("/account", GetAccount);
        gitHub.MapDelete("/account", Disconnect);
        gitHub.MapGet("/repositories", ListAvailable);

        RouteGroupBuilder workspaceRepositories = app.MapGroup("/workspaces/{workspaceId:guid}/repositories").WithTags("Sources");
        workspaceRepositories.MapPost("/", Add);
        workspaceRepositories.MapGet("/", List);
        app.MapDelete("/repositories/{id:guid}", Remove).WithTags("Sources");

        app.MapGet("/git-settings", GetSettings).WithTags("Sources");
        app.MapPut("/git-settings", SetSettings).WithTags("Sources");
    }

    /// <summary>
    /// Starts connecting your GitHub account through the host's GitHub App: enter the returned code at
    /// its verification address, then complete the connection every interval until you have.
    /// </summary>
    private static async Task<Results<Ok<GitHubConnectionStarted>, ProblemHttpResult>> StartConnection(
        ClaimsPrincipal principal,
        [FromServices] ISourcesApi api,
        CancellationToken ct)
    {
        Result<GitHubConnectionStarted> result = await api.StartGitHubConnectionAsync(principal.ToActor(), ct);
        return result.ToOk();
    }

    /// <summary>Connects your GitHub account once you approved at GitHub; until then, says how long to wait.</summary>
    private static async Task<Results<Ok<GitHubConnectionProgress>, ProblemHttpResult>> CompleteConnection(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] ISourcesApi api,
        CancellationToken ct)
    {
        Result<GitHubConnectionProgress> result = await api.CompleteGitHubConnectionAsync(principal.ToActor(), GitHubConnectionAttemptId.From(id), ct);
        return result.ToOk();
    }

    /// <summary>Your connected GitHub account.</summary>
    private static async Task<Results<Ok<GitHubAccount>, ProblemHttpResult>> GetAccount(
        ClaimsPrincipal principal,
        [FromServices] ISourcesApi api,
        CancellationToken ct)
    {
        Result<GitHubAccount> result = await api.GetGitHubAccountAsync(principal.ToActor(), ct);
        return result.ToOk();
    }

    /// <summary>Forgets your GitHub connection; you push nothing until you connect again.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Disconnect(
        ClaimsPrincipal principal,
        [FromServices] ISourcesApi api,
        CancellationToken ct)
    {
        Result result = await api.DisconnectGitHubAsync(principal.ToActor(), ct);
        return result.ToNoContent();
    }

    /// <summary>The repositories your GitHub connection reaches, and where to install the app on more.</summary>
    private static async Task<Results<Ok<AvailableRepositories>, ProblemHttpResult>> ListAvailable(
        ClaimsPrincipal principal,
        [FromServices] ISourcesApi api,
        CancellationToken ct)
    {
        Result<AvailableRepositories> result = await api.ListAvailableRepositoriesAsync(principal.ToActor(), ct);
        return result.ToOk();
    }

    /// <summary>
    /// Adds a GitHub repository your connection reaches to the workspace, such as <c>acme/api</c>, for
    /// its nooks to start with at <c>/work/&lt;name&gt;</c>. Managers only.
    /// </summary>
    private static async Task<Results<Created<RepositorySummary>, ProblemHttpResult>> Add(
        [FromRoute] Guid workspaceId,
        [FromBody] AddRepositoryRequest request,
        ClaimsPrincipal principal,
        [FromServices] ISourcesApi api,
        CancellationToken ct)
    {
        Result<RepositorySummary> result = await api.AddRepositoryAsync(principal.ToActor(), new AddRepository(WorkspaceId.From(workspaceId), request.FullName), ct);
        return result.ToCreated(repository => string.Create(CultureInfo.InvariantCulture, $"/repositories/{repository.Id.Value}"));
    }

    /// <summary>The workspace's repositories, by name.</summary>
    private static async Task<Results<Ok<IReadOnlyList<RepositorySummary>>, ProblemHttpResult>> List(
        [FromRoute] Guid workspaceId,
        ClaimsPrincipal principal,
        [FromServices] ISourcesApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<RepositorySummary>> result = await api.ListRepositoriesAsync(principal.ToActor(), WorkspaceId.From(workspaceId), ct);
        return result.ToOk();
    }

    /// <summary>Removes a repository from its workspace; nooks that have it keep their copy. Managers only.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Remove(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] ISourcesApi api,
        CancellationToken ct)
    {
        Result result = await api.RemoveRepositoryAsync(principal.ToActor(), RepositoryId.From(id), ct);
        return result.ToNoContent();
    }

    /// <summary>How your commits and branches look: author, committer, AiSloth as co-author, and the branch prefix.</summary>
    private static async Task<Results<Ok<GitSettings>, ProblemHttpResult>> GetSettings(
        ClaimsPrincipal principal,
        [FromServices] ISourcesApi api,
        CancellationToken ct)
    {
        Result<GitSettings> result = await api.GetGitSettingsAsync(principal.ToActor(), ct);
        return result.ToOk();
    }

    /// <summary>
    /// Sets how your commits and branches look, all at once: a null author is your GitHub account, a
    /// null committer the author.
    /// </summary>
    private static async Task<Results<Ok<GitSettings>, ProblemHttpResult>> SetSettings(
        [FromBody] GitSettingsRequest request,
        ClaimsPrincipal principal,
        [FromServices] ISourcesApi api,
        CancellationToken ct)
    {
        SetGitSettings command = new SetGitSettings(request.Author, request.Committer, request.AiSlothCoAuthor, request.BranchPrefix);
        Result<GitSettings> result = await api.SetGitSettingsAsync(principal.ToActor(), command, ct);
        return result.ToOk();
    }
}
