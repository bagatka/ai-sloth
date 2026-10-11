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
using Bagatka.ModelAccess;

namespace Bagatka.AiSloth.WebApi.Endpoints;

internal static class AgentAccountsEndpoints
{
    internal sealed record AddAgentAccountRequest(ModelAccessKind Kind, string Name, string Secret, Uri? Endpoint = null);

    internal sealed record StartSignInRequest(ModelAccessKind Kind, string Name);

    internal sealed record CompleteSignInRequest(string? Code = null);

    public static RouteGroupBuilder MapAgentAccountsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder workspaceAccounts = app.MapGroup("/workspaces/{workspaceId:guid}/agent-accounts").WithTags("Agent accounts");
        workspaceAccounts.MapPost("/", AddToWorkspace);
        workspaceAccounts.MapGet("/", List);
        workspaceAccounts.MapGet("/{id:guid}/usage", GetPlanUsage);

        app.MapGet("/agent-account-kinds", ListKinds).WithTags("Agent accounts");

        RouteGroupBuilder accounts = app.MapGroup("/agent-accounts").WithTags("Agent accounts");
        accounts.MapPost("/", AddOwn);
        accounts.MapDelete("/{id:guid}", Remove);
        accounts.MapPost("/sign-ins", StartSignIn);
        accounts.MapPost("/sign-ins/{id:guid}/complete", CompleteSignIn);
        return accounts;
    }

    /// <summary>
    /// Every kind of account, how each is added (with its secret, or by signing in at the vendor),
    /// whether it is personal only, and whether this host allows it. <c>GET /harnesses</c> says which
    /// harnesses run on each.
    /// </summary>
    private static async Task<Ok<IReadOnlyList<AgentAccountKindSummary>>> ListKinds(
        ClaimsPrincipal principal,
        [FromServices] IAgentAccountsApi api,
        CancellationToken ct)
    {
        IReadOnlyList<AgentAccountKindSummary> kinds = await api.ListKindsAsync(principal.ToActor(), ct);
        return TypedResults.Ok(kinds);
    }

    /// <summary>
    /// Adds an account at an agent vendor that every member of the workspace with Write can run agents
    /// on, such as an OpenAI or Anthropic API key, optionally at another endpoint that speaks the
    /// vendor's API, such as OpenRouter's. Managers only. The secret is never shown again.
    /// </summary>
    private static async Task<Results<Created<AgentAccountSummary>, ProblemHttpResult>> AddToWorkspace(
        [FromRoute] Guid workspaceId,
        [FromBody] AddAgentAccountRequest request,
        ClaimsPrincipal principal,
        [FromServices] IAgentAccountsApi api,
        CancellationToken ct)
    {
        AddAgentAccount command = new AddAgentAccount(WorkspaceId.From(workspaceId), request.Kind, request.Name, request.Secret, request.Endpoint);
        Result<AgentAccountSummary> result = await api.AddAsync(principal.ToActor(), command, ct);
        return result.ToCreated(PathOf);
    }

    /// <summary>
    /// Adds the caller's own account at an agent vendor with its secret, such as an API key or a Claude
    /// plan's token; only the caller can run agents on it. The secret is never shown again. ChatGPT and
    /// Copilot plans are added by signing in instead (<c>POST /agent-accounts/sign-ins</c>).
    /// </summary>
    private static async Task<Results<Created<AgentAccountSummary>, ProblemHttpResult>> AddOwn(
        [FromBody] AddAgentAccountRequest request,
        ClaimsPrincipal principal,
        [FromServices] IAgentAccountsApi api,
        CancellationToken ct)
    {
        AddAgentAccount command = new AddAgentAccount(WorkspaceId: null, request.Kind, request.Name, request.Secret, request.Endpoint);
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

    /// <summary>
    /// What the plan has used of its limits, asked of the vendor now: a ChatGPT or Claude plan's
    /// five-hour and weekly windows, or a Copilot plan's quotas or credits spent. Nothing for an API key.
    /// A conflict when the vendor asks to be asked later.
    /// </summary>
    private static async Task<Results<Ok<PlanUsage>, ProblemHttpResult>> GetPlanUsage(
        [FromRoute] Guid workspaceId,
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IAgentAccountsApi api,
        CancellationToken ct)
    {
        Result<PlanUsage> result = await api.GetPlanUsageAsync(principal.ToActor(), AgentAccountId.From(id), WorkspaceId.From(workspaceId), ct);
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

    /// <summary>
    /// Starts adding the caller's own plan by signing in at its vendor. A ChatGPT or Copilot plan: the
    /// person enters the returned code at the returned URL, OpenAI's or GitHub's, while the caller
    /// completes the sign-in every interval. A Claude plan: the person approves at the returned URL,
    /// Claude's, and the caller completes the sign-in with the code Claude's page shows, within ten
    /// minutes.
    /// </summary>
    private static async Task<Results<Ok<SignInStarted>, ProblemHttpResult>> StartSignIn(
        [FromBody] StartSignInRequest request,
        ClaimsPrincipal principal,
        [FromServices] IAgentAccountsApi api,
        CancellationToken ct)
    {
        StartSignIn command = new StartSignIn(request.Kind, request.Name);
        Result<SignInStarted> result = await api.StartSignInAsync(principal.ToActor(), command, ct);
        return result.ToOk();
    }

    /// <summary>
    /// Completes a sign-in, adding the account: with the code Claude's page showed, or, for a sign-in
    /// with a user code and without a body, once the person approved it; until then, the progress says
    /// when to ask again. A failed or expired sign-in starts again.
    /// </summary>
    private static async Task<Results<Ok<SignInProgress>, ProblemHttpResult>> CompleteSignIn(
        [FromRoute] Guid id,
        [FromBody] CompleteSignInRequest? request,
        ClaimsPrincipal principal,
        [FromServices] IAgentAccountsApi api,
        CancellationToken ct)
    {
        CompleteSignIn command = new CompleteSignIn(SignInId.From(id), request?.Code);
        Result<SignInProgress> result = await api.CompleteSignInAsync(principal.ToActor(), command, ct);
        return result.ToOk();
    }

    private static string PathOf(AgentAccountSummary account)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/agent-accounts/{account.Id.Value}");
    }
}
