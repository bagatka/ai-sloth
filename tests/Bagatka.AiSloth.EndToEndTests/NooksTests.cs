using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Nooks running in real containers with a real daemon, through the public API.
/// </summary>
public sealed class NooksTests(ControlPlane controlPlane) : IDisposable
{
    private readonly HttpClient _alice = controlPlane.ClientFor("alice-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_new_nook_runs_a_process_and_reports_its_output_and_exit()
    {
        NookSummary nook = await CreateNookAsync();
        Assert.Equal(NookStatus.Creating, nook.Status);

        ProcessSummary process = await StartAsync(nook, "sh", "-c", "echo hello; echo oops >&2; exit 3");
        Run run = await WatchToExitAsync(nook, process, fromOffset: 0);

        Assert.Equal("hello\n", run.StandardOutput);
        Assert.Equal("oops\n", run.StandardError);
        Assert.Equal(3, run.ExitCode);
        Assert.Equal(NookStatus.Running, (await GetAsync(nook)).Status);
    }

    [Fact]
    public async Task A_running_nook_reports_how_full_its_disk_is()
    {
        NookSummary nook = await CreateNookAsync();
        await StartAsync(nook, "true");

        DiskUsage disk = await Api.EventuallyAsync(async () => (await GetAsync(nook)).Disk);

        Assert.InRange(disk.AvailableBytes, 0, disk.TotalBytes);
    }

    [Fact]
    public async Task Input_reaches_a_running_process_and_stopping_ends_it_politely()
    {
        NookSummary nook = await CreateNookAsync();
        ProcessSummary process = await StartAsync(nook, "cat");
        await using IAsyncEnumerator<ProcessEvent> watching = WatchAsync(nook, process, fromOffset: 0, Ct).GetAsyncEnumerator(Ct);

        await Api.ExpectAsync(await _alice.SendPostAsync(PathOf(nook, process) + "/input", new { data = Encoding.UTF8.GetBytes("ping\n") }), HttpStatusCode.NoContent);
        Assert.True(await watching.MoveNextAsync());
        ProcessOutput echoed = Assert.IsType<ProcessOutput>(watching.Current.Value);
        await Api.ExpectAsync(await _alice.SendPostAsync(PathOf(nook, process) + "/stop", new { }), HttpStatusCode.NoContent);
        Assert.True(await watching.MoveNextAsync());

        Assert.Equal("ping\n", Encoding.UTF8.GetString(echoed.Data.Span));
        Assert.Equal(143, Assert.IsType<ProcessExited>(watching.Current.Value).ExitCode);
    }

    [Fact]
    public async Task A_watch_replays_output_from_any_offset()
    {
        NookSummary nook = await CreateNookAsync();
        ProcessSummary process = await StartAsync(nook, "printf", "abcdef");
        await WatchToExitAsync(nook, process, fromOffset: 0);

        Run replay = await WatchToExitAsync(nook, process, fromOffset: 3);

        Assert.Equal("def", replay.StandardOutput);
        Assert.Equal(0, replay.ExitCode);
    }

    [Fact]
    public async Task Processes_are_listed_newest_first()
    {
        NookSummary nook = await CreateNookAsync();
        ProcessSummary first = await StartAsync(nook, "true");
        ProcessSummary second = await StartAsync(nook, "true");

        Page<ProcessSummary> page = await Api.ReadAsync<Page<ProcessSummary>>(await _alice.SendGetAsync(PathOf(nook) + "/processes"), HttpStatusCode.OK);

        Assert.Equal([second.Id, first.Id], page.Items.Select(process => process.Id));
    }

    [Fact]
    public async Task Only_members_of_its_workspace_can_use_a_nook()
    {
        NookSummary nook = await CreateNookAsync();
        using HttpClient bob = controlPlane.ClientFor("bob-" + Guid.CreateVersion7());

        Problem get = await Api.ProblemAsync(await bob.SendGetAsync(PathOf(nook)), HttpStatusCode.NotFound);
        Problem start = await Api.ProblemAsync(await bob.SendPostAsync(PathOf(nook) + "/processes", new { command = "true" }), HttpStatusCode.NotFound);
        Problem list = await Api.ProblemAsync(await bob.SendGetAsync(WorkspaceNooksPath(nook.WorkspaceId)), HttpStatusCode.NotFound);
        Problem create = await Api.ProblemAsync(await bob.SendPostAsync(WorkspaceNooksPath(nook.WorkspaceId), new { provider = "docker" }), HttpStatusCode.NotFound);

        Assert.Equal(NooksErrors.NotFound.Code, get.Code);
        Assert.Equal(NooksErrors.NotFound.Code, start.Code);
        Assert.Equal(WorkspacesErrors.NotFound.Code, list.Code);
        Assert.Equal(WorkspacesErrors.NotFound.Code, create.Code);
    }

    [Fact]
    public async Task A_nook_needs_a_known_provider_and_a_process_needs_a_command()
    {
        NookSummary nook = await CreateNookAsync();

        Problem unknownProvider = await Api.ProblemAsync(await _alice.SendPostAsync(WorkspaceNooksPath(nook.WorkspaceId), new { provider = "nowhere" }), HttpStatusCode.BadRequest);
        Problem noCommand = await Api.ProblemAsync(await _alice.SendPostAsync(PathOf(nook) + "/processes", new { command = "" }), HttpStatusCode.BadRequest);

        Assert.True(unknownProvider.Errors?.ContainsKey("provider"));
        Assert.True(noCommand.Errors?.ContainsKey("command"));
    }

    [Fact]
    public async Task Deleting_a_nook_removes_it()
    {
        NookSummary nook = await CreateNookAsync();
        await StartAsync(nook, "true");

        HttpResponseMessage deleted = await _alice.DeleteAsync(new Uri(PathOf(nook), UriKind.Relative), Ct);

        await Api.ExpectAsync(deleted, HttpStatusCode.NoContent);
        await Api.EventuallyAsync(async () => (await _alice.SendGetAsync(PathOf(nook))).StatusCode == HttpStatusCode.NotFound ? "gone" : null);
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    private static string PathOf(NookSummary nook)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Id.Value}");
    }

    private static string PathOf(NookSummary nook, ProcessSummary process)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/nooks/{nook.Id.Value}/processes/{process.Id.Value}");
    }

    private static string WorkspaceNooksPath(WorkspaceId workspace)
    {
        return string.Create(CultureInfo.InvariantCulture, $"/workspaces/{workspace.Value}/nooks");
    }

    private async Task<NookSummary> CreateNookAsync()
    {
        WorkspaceSummary workspace = await Api.ReadAsync<WorkspaceSummary>(await _alice.SendPostAsync("/workspaces", new { name = "Acme" }), HttpStatusCode.Created);
        return await Api.ReadAsync<NookSummary>(await _alice.SendPostAsync(WorkspaceNooksPath(workspace.Id), new { provider = "docker" }), HttpStatusCode.Created);
    }

    private async Task<NookSummary> GetAsync(NookSummary nook)
    {
        return await Api.ReadAsync<NookSummary>(await _alice.SendGetAsync(PathOf(nook)), HttpStatusCode.OK);
    }

    // Waits for the nook's daemon on the first call.
    private async Task<ProcessSummary> StartAsync(NookSummary nook, string command, params string[] arguments)
    {
        return await Api.ReadAsync<ProcessSummary>(await _alice.SendPostAsync(PathOf(nook) + "/processes", new { command, arguments }), HttpStatusCode.OK);
    }

    // The process's events, read from the server-sent events stream.
    private async IAsyncEnumerable<ProcessEvent> WatchAsync(NookSummary nook, ProcessSummary process, long fromOffset, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Uri output = new Uri(string.Create(CultureInfo.InvariantCulture, $"{PathOf(nook, process)}/output?fromOffset={fromOffset}"), UriKind.Relative);
        using HttpResponseMessage response = await _alice.GetAsync(output, HttpCompletionOption.ResponseHeadersRead, ct);
        await Api.ExpectAsync(response, HttpStatusCode.OK);
        await using Stream stream = await response.Content.ReadAsStreamAsync(ct);
        await foreach (SseItem<string> item in SseParser.Create(stream).EnumerateAsync(ct))
        {
            yield return item.EventType switch
            {
                "output" => new ProcessEvent(JsonSerializer.Deserialize<ProcessOutput>(item.Data, FoundationJson.Options)!),
                "exit" => new ProcessEvent(JsonSerializer.Deserialize<ProcessExited>(item.Data, FoundationJson.Options)!),
                _ => throw new InvalidOperationException("Unexpected event " + item.EventType),
            };
        }
    }

    private async Task<Run> WatchToExitAsync(NookSummary nook, ProcessSummary process, long fromOffset)
    {
        StringBuilder standardOutput = new StringBuilder();
        StringBuilder standardError = new StringBuilder();
        await foreach (ProcessEvent processEvent in WatchAsync(nook, process, fromOffset, Ct))
        {
            switch (processEvent)
            {
                case ProcessOutput output:
                    (output.Channel == OutputChannel.StandardOutput ? standardOutput : standardError).Append(Encoding.UTF8.GetString(output.Data.Span));
                    break;
                case ProcessExited exited:
                    return new Run(standardOutput.ToString(), standardError.ToString(), exited.ExitCode);
            }
        }

        throw new InvalidOperationException("The watch ended without the process's exit.");
    }

    private sealed record Run(string StandardOutput, string StandardError, int ExitCode);
}
