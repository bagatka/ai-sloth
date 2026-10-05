using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bagatka.AiSloth.WebApi.Endpoints;

internal static class NooksEndpoints
{
    internal sealed record CreateNookRequest(string Provider);

    internal sealed record StartProcessRequest(
        string Command,
        IReadOnlyList<string>? Arguments = null,
        string? WorkingDirectory = null,
        OutputRetention? Retention = null,
        IReadOnlyDictionary<string, string>? Environment = null);

    internal sealed record SendInputRequest(ReadOnlyMemory<byte> Data);

    public static RouteGroupBuilder MapNooksEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder workspaceNooks = app.MapGroup("/workspaces/{workspaceId:guid}/nooks").WithTags("Nooks");
        workspaceNooks.MapPost("/", Create);
        workspaceNooks.MapGet("/", List);
        app.MapGet("/workspaces/{workspaceId:guid}/providers", ListProviders).WithTags("Nooks");

        RouteGroupBuilder nooks = app.MapGroup("/nooks").WithTags("Nooks");
        nooks.MapGet("/{id:guid}", Get);
        nooks.MapDelete("/{id:guid}", Delete);
        nooks.MapPost("/{nookId:guid}/processes", StartProcess);
        nooks.MapGet("/{nookId:guid}/processes", ListProcesses);
        nooks.MapGet("/{nookId:guid}/processes/{processId:guid}/output", WatchProcess);
        nooks.MapPost("/{nookId:guid}/processes/{processId:guid}/input", SendInput);
        nooks.MapPost("/{nookId:guid}/processes/{processId:guid}/stop", StopProcess);
        return nooks;
    }

    /// <summary>
    /// Creates a nook without an agent in the workspace on one of its providers, such as <c>docker</c>,
    /// for running processes; it starts in the background. A chat creates a nook of its own instead
    /// (<c>POST /workspaces/{id}/chats</c>).
    /// </summary>
    private static async Task<Results<Created<NookSummary>, ProblemHttpResult>> Create(
        [FromRoute] Guid workspaceId,
        [FromBody] CreateNookRequest request,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result<NookSummary> result = await api.CreateAsync(principal.ToActor(), new CreateNook(WorkspaceId.From(workspaceId), request.Provider, Harness: null), ct);
        return result.ToCreated(nook => string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Id.Value}"));
    }

    /// <summary>The providers the workspace's nooks can run on, with the ID <c>provider</c> takes when creating one.</summary>
    private static async Task<Results<Ok<IReadOnlyList<ProviderSummary>>, ProblemHttpResult>> ListProviders(
        [FromRoute] Guid workspaceId,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<ProviderSummary>> result = await api.ListProvidersAsync(principal.ToActor(), WorkspaceId.From(workspaceId), ct);
        return result.ToOk();
    }

    /// <summary>The workspace's nooks, newest first.</summary>
    private static async Task<Results<Ok<Page<NookSummary>>, ProblemHttpResult>> List(
        [FromRoute] Guid workspaceId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result<Page<NookSummary>> result = await api.ListAsync(principal.ToActor(), WorkspaceId.From(workspaceId), Paging.Request(cursor, limit), ct);
        return result.ToOk();
    }

    /// <summary>A nook, with its status and how full its disk is.</summary>
    private static async Task<Results<Ok<NookSummary>, ProblemHttpResult>> Get(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result<NookSummary> result = await api.GetAsync(principal.ToActor(), NookId.From(id), ct);
        return result.ToOk();
    }

    /// <summary>Deletes a nook and its files. It shows Deleting until its provider confirms, then disappears.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Delete(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result result = await api.DeleteAsync(principal.ToActor(), NookId.From(id), ct);
        return result.ToNoContent();
    }

    /// <summary>
    /// Starts a program in the nook, without a shell, waiting for a new nook to be ready. It runs
    /// until it exits or is stopped; output is kept as <c>retention</c> says, <c>Recent</c> by default.
    /// </summary>
    private static async Task<Results<Ok<ProcessSummary>, ProblemHttpResult>> StartProcess(
        [FromRoute] Guid nookId,
        [FromBody] StartProcessRequest request,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        StartProcess command = new StartProcess(
            NookId.From(nookId),
            request.Command,
            request.Arguments ?? [],
            request.WorkingDirectory,
            request.Retention ?? OutputRetention.Recent,
            request.Environment ?? new Dictionary<string, string>(StringComparer.Ordinal));
        Result<ProcessSummary> result = await api.StartProcessAsync(principal.ToActor(), command, ct);
        return result.ToOk();
    }

    /// <summary>The nook's processes, newest first.</summary>
    private static async Task<Results<Ok<Page<ProcessSummary>>, ProblemHttpResult>> ListProcesses(
        [FromRoute] Guid nookId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result<Page<ProcessSummary>> result = await api.ListProcessesAsync(principal.ToActor(), NookId.From(nookId), Paging.Request(cursor, limit), ct);
        return result.ToOk();
    }

    /// <summary>
    /// Streams a process's output from <c>fromOffset</c> (0 by default) as server-sent events:
    /// <c>output</c> events carry the offset, channel, and base64 data, and a final <c>exit</c> event
    /// carries the exit code. Disconnecting ends the watch, never the process.
    /// </summary>
    private static async Task<Results<ServerSentEventsResult<object>, ProblemHttpResult>> WatchProcess(
        [FromRoute] Guid nookId,
        [FromRoute] Guid processId,
        [FromQuery] long? fromOffset,
        ClaimsPrincipal principal,
        HttpResponse response,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        WatchProcess command = new WatchProcess(NookId.From(nookId), ProcessId.From(processId), fromOffset ?? 0);
        Result<IAsyncEnumerable<ProcessEvent>> result = await api.WatchProcessAsync(principal.ToActor(), command, ct);
        if (result.Failed)
        {
            return result.Error.ToProblem();
        }

        return TypedResults.ServerSentEvents(AsServerSentEvents(response, result.Output, ct));
    }

    /// <summary>Writes base64 <c>data</c>, at most 64 KiB, to a running process's standard input.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> SendInput(
        [FromRoute] Guid nookId,
        [FromRoute] Guid processId,
        [FromBody] SendInputRequest request,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result result = await api.SendInputAsync(principal.ToActor(), new SendInput(NookId.From(nookId), ProcessId.From(processId), request.Data), ct);
        return result.ToNoContent();
    }

    /// <summary>Stops a process: politely first, then forcibly. Stopping one that exited succeeds.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> StopProcess(
        [FromRoute] Guid nookId,
        [FromRoute] Guid processId,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result result = await api.StopProcessAsync(principal.ToActor(), new StopProcess(NookId.From(nookId), ProcessId.From(processId)), ct);
        return result.ToNoContent();
    }

    private static async IAsyncEnumerable<SseItem<object>> AsServerSentEvents(HttpResponse response, IAsyncEnumerable<ProcessEvent> events, [EnumeratorCancellation] CancellationToken ct)
    {
        // The headers go out now, not with the first output, so a client watching a quiet process knows it is connected.
        await response.Body.FlushAsync(ct);
        await foreach (ProcessEvent processEvent in events.WithCancellation(ct))
        {
            yield return processEvent switch
            {
                ProcessOutput output => new SseItem<object>(output, "output"),
                ProcessExited exited => new SseItem<object>(exited, "exit"),
            };
        }
    }
}
