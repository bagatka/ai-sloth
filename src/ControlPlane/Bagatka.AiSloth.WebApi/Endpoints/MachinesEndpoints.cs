using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bagatka.AiSloth.WebApi.Endpoints;

internal static class MachinesEndpoints
{
    internal sealed record AddMachineRequest(string Name);

    public static RouteGroupBuilder MapMachinesEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder workspaceMachines = app.MapGroup("/workspaces/{workspaceId:guid}/machines").WithTags("Machines");
        workspaceMachines.MapPost("/", Add);
        workspaceMachines.MapGet("/", List);

        RouteGroupBuilder machines = app.MapGroup("/machines").WithTags("Machines");
        machines.MapGet("/{id:guid}", Get);
        machines.MapDelete("/{id:guid}", Remove);
        return machines;
    }

    /// <summary>
    /// Adds a machine to the workspace and returns the one-time code that registers it with
    /// <c>sloth machine connect</c>, valid for an hour. Owners only.
    /// </summary>
    private static async Task<Results<Created<MachineRegistration>, ProblemHttpResult>> Add(
        [FromRoute] Guid workspaceId,
        [FromBody] AddMachineRequest request,
        ClaimsPrincipal principal,
        [FromServices] IMachinesApi api,
        CancellationToken ct)
    {
        Result<MachineRegistration> result = await api.AddAsync(principal.ToActor(), new AddMachine(WorkspaceId.From(workspaceId), request.Name), ct);
        return result.ToCreated(added => string.Create(CultureInfo.InvariantCulture, $"/machines/{added.Machine.Id.Value}"));
    }

    /// <summary>The workspace's machines, oldest first, and whether each is online.</summary>
    private static async Task<Results<Ok<IReadOnlyList<MachineSummary>>, ProblemHttpResult>> List(
        [FromRoute] Guid workspaceId,
        ClaimsPrincipal principal,
        [FromServices] IMachinesApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<MachineSummary>> result = await api.ListAsync(principal.ToActor(), WorkspaceId.From(workspaceId), ct);
        return result.ToOk();
    }

    /// <summary>A machine of a workspace the caller is a member of; others get 404.</summary>
    private static async Task<Results<Ok<MachineSummary>, ProblemHttpResult>> Get(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IMachinesApi api,
        CancellationToken ct)
    {
        Result<MachineSummary> result = await api.GetAsync(principal.ToActor(), MachineId.From(id), ct);
        return result.ToOk();
    }

    /// <summary>Removes a machine: its credential stops working and its connection ends. Owners only.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Remove(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IMachinesApi api,
        CancellationToken ct)
    {
        Result result = await api.RemoveAsync(principal.ToActor(), MachineId.From(id), ct);
        return result.ToNoContent();
    }
}
