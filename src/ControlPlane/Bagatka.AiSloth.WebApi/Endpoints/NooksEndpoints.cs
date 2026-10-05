using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
    internal sealed record CreateNookRequest(string Provider, IReadOnlyList<NookRepository>? Repositories = null, NookId? CopyOf = null, int? Checkpoint = null);

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
        nooks.MapGet("/{id:guid}/download", Download);
        nooks.MapGet("/{nookId:guid}/checkpoints", ListCheckpoints);
        nooks.MapGet("/{nookId:guid}/setup", GetSetup);
        nooks.MapPost("/{id:guid}/wake", Wake);
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
        Result<NookSummary> result = await api.CreateAsync(principal.ToActor(), new CreateNook(WorkspaceId.From(workspaceId), request.Provider, Harness: null, request.Repositories ?? [], request.CopyOf, request.Checkpoint, KeptPaths: []), ct);
        return result.ToCreated(nook => string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Id.Value}"));
    }

    /// <summary>
    /// The nook's files as a gzipped tar archive: one of its sources with <c>source</c>, or all of
    /// <c>/work</c>; as they are now, or at the checkpoint numbered <c>checkpoint</c>. Anyone who
    /// sees the nook may.
    /// </summary>
    private static async Task<Results<PushStreamHttpResult, ProblemHttpResult>> Download(
        [FromRoute] Guid id,
        [FromQuery] string? source,
        [FromQuery] int? checkpoint,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        // The archive is complete before it is sent, so a failure midway is a problem, not a broken
        // file. Sending it deletes it.
        string path = Path.GetTempFileName();
        Result downloaded;
        await using (FileStream archive = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, bufferSize: 81920, FileOptions.Asynchronous))
        {
            downloaded = await api.DownloadAsync(principal.ToActor(), new DownloadFiles(NookId.From(id), source, checkpoint), archive, ct);
        }

        if (downloaded.Failed)
        {
            File.Delete(path);
            return downloaded.Error.ToProblem();
        }

        string at = checkpoint is int number ? "@" + number.ToString(CultureInfo.InvariantCulture) : string.Empty;
        string name = (source ?? "work") + "-" + id.ToString("N", CultureInfo.InvariantCulture)[^6..] + at + ".tar.gz";
        return TypedResults.Stream(
            async body =>
            {
                await using FileStream archive = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None, bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
                await archive.CopyToAsync(body);
            },
            "application/gzip",
            name);
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
    /// Wakes the nook when it sleeps, without waiting for it, and keeps it awake for a while, as any use
    /// does: for clients to call when someone is about to use it, such as when a person opens its chat
    /// or starts typing. Asleep, a nook costs nothing; any operation wakes it anyway, only later.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Wake(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result result = await api.WakeAsync(principal.ToActor(), new WakeNook(NookId.From(id), KeepAwakeFor: null), ct);
        return result.ToNoContent();
    }

    /// <summary>
    /// The nook's setup: the <c>.agents/setup</c> and <c>.agents/resume</c> scripts in its files, which run
    /// whenever the nook gets its files, and their latest run, whose process's output says what they did.
    /// </summary>
    private static async Task<Results<Ok<NookSetup>, ProblemHttpResult>> GetSetup(
        [FromRoute] Guid nookId,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result<NookSetup> result = await api.GetSetupAsync(principal.ToActor(), NookId.From(nookId), ct);
        return result.ToOk();
    }

    /// <summary>The nook's checkpoints, newest first: one after each of its chat's turns.</summary>
    private static async Task<Results<Ok<Page<CheckpointSummary>>, ProblemHttpResult>> ListCheckpoints(
        [FromRoute] Guid nookId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        ClaimsPrincipal principal,
        [FromServices] INooksApi api,
        CancellationToken ct)
    {
        Result<Page<CheckpointSummary>> result = await api.ListCheckpointsAsync(principal.ToActor(), NookId.From(nookId), Paging.Request(cursor, limit), ct);
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
