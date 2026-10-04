using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bagatka.AiSloth.WebApi.Endpoints;

internal static class AgentAccountsEndpoints
{
    internal sealed record AddAgentAccountRequest(AgentAccountKind Kind, string Name, string Secret);

    public static RouteGroupBuilder MapAgentAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder workspaceAccounts = app.MapGroup("/workspaces/{workspaceId:guid}/agent-accounts").WithTags("Agent accounts");
        workspaceAccounts.MapPost("/", AddToWorkspace);
        workspaceAccounts.MapGet("/", List);

        RouteGroupBuilder accounts = app.MapGroup("/agent-accounts").WithTags("Agent accounts");
        accounts.MapPost("/", AddOwn);
        accounts.MapDelete("/{id:guid}", Remove);
        return accounts;
    }

    /// <summary>
    /// Adds an account at an agent vendor that every member of the workspace can run agents on, such
    /// as an Anthropic API key. Owners only. The secret is never shown again.
    /// </summary>
    private static async Task<Results<Created<AgentAccountSummary>, ProblemHttpResult>> AddToWorkspace(
        [FromRoute] Guid workspaceId,
        [FromBody] AddAgentAccountRequest request,
        ClaimsPrincipal principal,
        [FromServices] IAgentAccountsApi api,
        CancellationToken ct)
    {
        AddAgentAccount command = new AddAgentAccount(WorkspaceId.From(workspaceId), request.Kind, request.Name, request.Secret);
        Result<AgentAccountSummary> result = await api.AddAsync(principal.ToActor(), command, ct);
        return result.ToCreated(PathOf);
    }

    /// <summary>
    /// Adds the caller's own account at an agent vendor, such as a Copilot token; only the caller can
    /// run agents on it. The secret is never shown again.
    /// </summary>
    private static async Task<Results<Created<AgentAccountSummary>, ProblemHttpResult>> AddOwn(
        [FromBody] AddAgentAccountRequest request,
        ClaimsPrincipal principal,
        [FromServices] IAgentAccountsApi api,
        CancellationToken ct)
    {
        AddAgentAccount command = new AddAgentAccount(WorkspaceId: null, request.Kind, request.Name, request.Secret);
        Result<AgentAccountSummary> result = await api.AddAsync(principal.ToActor(), command, ct);
        return result.ToCreated(PathOf);
    }

    /// <summary>The accounts the caller can run agents on in the workspace: the workspace's, then the caller's own.</summary>
    private static async Task<Results<Ok<IReadOnlyList<AgentAccountSummary>>, ProblemHttpResult>> List(
        [FromRoute] Guid workspaceId,
        ClaimsPrincipal principal,
        [FromServices] IAgentAccountsApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<AgentAccountSummary>> result = await api.ListAsync(principal.ToActor(), WorkspaceId.From(workspaceId), ct);
        return result.ToOk();
    }

    /// <summary>Removes an account: the caller's own, or a workspace's by its owners. Calls through the model gateway stop at once.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Remove(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IAgentAccountsApi api,
        CancellationToken ct)
    {
        Result result = await api.RemoveAsync(principal.ToActor(), AgentAccountId.From(id), ct);
        return result.ToNoContent();
    }

    private static string PathOf(AgentAccountSummary account)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/agent-accounts/{account.Id.Value}");
    }
}
