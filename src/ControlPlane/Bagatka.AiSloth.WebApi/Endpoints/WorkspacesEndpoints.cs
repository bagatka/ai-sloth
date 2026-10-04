using System;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bagatka.AiSloth.WebApi.Endpoints;

internal static class WorkspacesEndpoints
{
    internal sealed record CreateWorkspaceRequest(string Name);

    public static RouteGroupBuilder MapWorkspacesEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder workspaces = app.MapGroup("/workspaces").WithTags("Workspaces");
        workspaces.MapPost("/", Create);
        workspaces.MapGet("/", ListMine);
        workspaces.MapGet("/{id:guid}", Get);
        return workspaces;
    }

    /// <summary>Creates a workspace; the caller manages it.</summary>
    private static async Task<Results<Created<WorkspaceSummary>, ProblemHttpResult>> Create(
        [FromBody] CreateWorkspaceRequest request,
        ClaimsPrincipal principal,
        [FromServices] IWorkspacesApi api,
        CancellationToken ct)
    {
        Result<WorkspaceSummary> result = await api.CreateAsync(principal.ToActor(), new CreateWorkspace(request.Name), ct);
        return result.ToCreated(workspace => string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Id.Value}"));
    }

    /// <summary>The caller's workspaces, oldest first.</summary>
    private static async Task<Results<Ok<Page<WorkspaceSummary>>, ProblemHttpResult>> ListMine(
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        ClaimsPrincipal principal,
        [FromServices] IWorkspacesApi api,
        CancellationToken ct)
    {
        Result<Page<WorkspaceSummary>> result = await api.ListMineAsync(principal.ToActor(), Paging.Request(cursor, limit), ct);
        return result.ToOk();
    }

    /// <summary>A workspace the caller has access to; others get 404.</summary>
    private static async Task<Results<Ok<WorkspaceSummary>, ProblemHttpResult>> Get(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IWorkspacesApi api,
        CancellationToken ct)
    {
        Result<WorkspaceSummary> result = await api.GetAsync(principal.ToActor(), WorkspaceId.From(id), ct);
        return result.ToOk();
    }
}
