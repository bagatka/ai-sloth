using System;
using System.Collections.Generic;
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

// Who may do what with a workspace or a nook: invites, and the people given access.
internal static class AccessEndpoints
{
    internal sealed record CreateInviteRequest(AccessLevel Access);

    internal sealed record AcceptInviteRequest(string Code);

    public static void MapAccessEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder workspace = app.MapGroup("/workspaces/{workspaceId:guid}").WithTags("Access");
        workspace.MapPost("/invites", InviteToWorkspace);
        workspace.MapGet("/access", ListWorkspaceAccess);
        workspace.MapDelete("/access/{userId:guid}", RevokeWorkspaceAccess);

        RouteGroupBuilder nook = app.MapGroup("/nooks/{nookId:guid}").WithTags("Access");
        nook.MapPost("/invites", InviteToNook);
        nook.MapGet("/access", ListNookAccess);
        nook.MapDelete("/access/{userId:guid}", RevokeNookAccess);

        // The code goes in the body, not the path, so it stays out of request logs.
        app.MapPost("/invites/accept", Accept).WithTags("Access");
    }

    /// <summary>
    /// Creates a one-time invite to the workspace, valid for 7 days: Read, Write, or Manage. Only its
    /// managers may. The code is shown only in this response.
    /// </summary>
    private static async Task<Results<Ok<Invite>, ProblemHttpResult>> InviteToWorkspace(
        [FromRoute] Guid workspaceId,
        [FromBody] CreateInviteRequest request,
        ClaimsPrincipal principal,
        [FromServices] IWorkspacesApi api,
        CancellationToken ct)
    {
        CreateInvite command = new CreateInvite(Resource.Workspace(WorkspaceId.From(workspaceId)), request.Access);
        Result<Invite> result = await api.InviteAsync(principal.ToActor(), command, ct);
        return result.ToOk();
    }

    /// <summary>
    /// Creates a one-time invite to the nook alone, valid for 7 days. Only the people who manage the
    /// nook, such as its workspace's managers, may.
    /// </summary>
    private static async Task<Results<Ok<Invite>, ProblemHttpResult>> InviteToNook(
        [FromRoute] Guid nookId,
        [FromBody] CreateInviteRequest request,
        ClaimsPrincipal principal,
        [FromServices] IWorkspacesApi api,
        CancellationToken ct)
    {
        CreateInvite command = new CreateInvite(Resource.Nook(nookId), request.Access);
        Result<Invite> result = await api.InviteAsync(principal.ToActor(), command, ct);
        return result.ToOk();
    }

    /// <summary>Gives the caller the invite's access; the code never works again. Returns what it gave access to.</summary>
    private static async Task<Results<Ok<Resource>, ProblemHttpResult>> Accept(
        [FromBody] AcceptInviteRequest request,
        ClaimsPrincipal principal,
        [FromServices] IWorkspacesApi api,
        CancellationToken ct)
    {
        Result<Resource> result = await api.AcceptInviteAsync(principal.ToActor(), new AcceptInvite(request.Code), ct);
        return result.ToOk();
    }

    /// <summary>The people given access to the workspace, by user ID.</summary>
    private static async Task<Results<Ok<IReadOnlyList<GrantSummary>>, ProblemHttpResult>> ListWorkspaceAccess(
        [FromRoute] Guid workspaceId,
        ClaimsPrincipal principal,
        [FromServices] IWorkspacesApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<GrantSummary>> result = await api.ListGrantsAsync(principal.ToActor(), Resource.Workspace(WorkspaceId.From(workspaceId)), ct);
        return result.ToOk();
    }

    /// <summary>The people given access to the nook itself; its workspace's people have access too.</summary>
    private static async Task<Results<Ok<IReadOnlyList<GrantSummary>>, ProblemHttpResult>> ListNookAccess(
        [FromRoute] Guid nookId,
        ClaimsPrincipal principal,
        [FromServices] IWorkspacesApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<GrantSummary>> result = await api.ListGrantsAsync(principal.ToActor(), Resource.Nook(nookId), ct);
        return result.ToOk();
    }

    /// <summary>Ends someone's access to the workspace. Only its managers may, and the last manager stays.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeWorkspaceAccess(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid userId,
        ClaimsPrincipal principal,
        [FromServices] IWorkspacesApi api,
        CancellationToken ct)
    {
        RevokeAccess command = new RevokeAccess(Resource.Workspace(WorkspaceId.From(workspaceId)), UserId.From(userId));
        Result result = await api.RevokeAsync(principal.ToActor(), command, ct);
        return result.ToNoContent();
    }

    /// <summary>Ends someone's access given to the nook itself.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> RevokeNookAccess(
        [FromRoute] Guid nookId,
        [FromRoute] Guid userId,
        ClaimsPrincipal principal,
        [FromServices] IWorkspacesApi api,
        CancellationToken ct)
    {
        RevokeAccess command = new RevokeAccess(Resource.Nook(nookId), UserId.From(userId));
        Result result = await api.RevokeAsync(principal.ToActor(), command, ct);
        return result.ToNoContent();
    }
}
