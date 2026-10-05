using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Secrets.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bagatka.AiSloth.WebApi.Endpoints;

internal static class SecretsEndpoints
{
    internal sealed record SetSecretRequest(string Value);

    public static RouteGroupBuilder MapSecretsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder secrets = app.MapGroup("/workspaces/{workspaceId:guid}/secrets").WithTags("Secrets");
        secrets.MapGet("/", List);
        secrets.MapPut("/{name}", Set);
        secrets.MapDelete("/{name}", Remove);
        return secrets;
    }

    /// <summary>
    /// The workspace's secrets, by name, without their values: environment variables every process in
    /// its nooks gets, agents included, such as <c>GH_TOKEN</c> for <c>gh</c>.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<SecretSummary>>, ProblemHttpResult>> List(
        [FromRoute] Guid workspaceId,
        ClaimsPrincipal principal,
        [FromServices] ISecretsApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<SecretSummary>> result = await api.ListAsync(principal.ToActor(), WorkspaceId.From(workspaceId), ct);
        return result.ToOk();
    }

    /// <summary>
    /// Sets a secret, adding it or replacing its value, for every process started in the workspace's
    /// nooks from now on. Managers only. Everyone who may write in a nook can read its secrets, so set
    /// only what the workspace's people may use. The value is never shown again.
    /// </summary>
    private static async Task<Results<Ok<SecretSummary>, ProblemHttpResult>> Set(
        [FromRoute] Guid workspaceId,
        [FromRoute] string name,
        [FromBody] SetSecretRequest request,
        ClaimsPrincipal principal,
        [FromServices] ISecretsApi api,
        CancellationToken ct)
    {
        Result<SecretSummary> result = await api.SetAsync(principal.ToActor(), new SetSecret(WorkspaceId.From(workspaceId), name, request.Value), ct);
        return result.ToOk();
    }

    /// <summary>Removes a secret; processes already running keep it. Managers only.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Remove(
        [FromRoute] Guid workspaceId,
        [FromRoute] string name,
        ClaimsPrincipal principal,
        [FromServices] ISecretsApi api,
        CancellationToken ct)
    {
        Result result = await api.RemoveAsync(principal.ToActor(), new RemoveSecret(WorkspaceId.From(workspaceId), name), ct);
        return result.ToNoContent();
    }
}
