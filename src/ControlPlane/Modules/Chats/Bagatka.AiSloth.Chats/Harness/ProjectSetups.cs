using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Chats.Harness;

// A project's setup as chats work with it (Nooks runs it): following a run to its end, testing the
// setup in a fresh nook, and what an agent is asked to prepare it and to fix it.
internal sealed class ProjectSetups(IServiceScopeFactory scopes, TimeProvider time, ILogger<ProjectSetups> logger)
{
    // How much of a failed setup's output a chat shows: its end, where the failure is.
    public const int MaxOutput = 2000;

    // How many tests one request to prepare gets: each failed one but the last goes back to the agent.
    public const int MaxTests = 3;

    // How long a fresh nook may take to start, such as while its machine pulls the image.
    private static readonly TimeSpan StartPatience = TimeSpan.FromMinutes(10);

    // What the agent is asked when someone prepares a chat's project.
    public const string PrepareRequest = """
        Prepare this project so that new nooks start fast, with everything it needs installed and running.

        Write a `.agents/setup` and a `.agents/resume` script, executable and starting with `#!/bin/sh` or `#!/bin/bash`, in each project folder directly in /work, or in /work itself for a project there:

        - `.agents/setup` installs everything the project needs to build, test, and run: system packages, language runtimes and tools at the versions the project pins, and its dependencies. AiSloth runs it as root in the project's folder, with the workspace's secrets, whenever a nook gets the project's files, before its agent starts, and gives it 30 minutes.
        - `.agents/resume` starts the services the project needs, such as databases with `docker compose up -d`, and returns once they are up. It runs after every setup, and gets 5 minutes.
        - Both must be safe to run again, and fast the second time: skip what is already installed, and install dependencies again only when their lockfiles changed.
        - A fresh nook is Ubuntu with Docker, git, and curl. Don't rely on anything installed by hand in this nook.
        - Never print or store secrets.

        Run both scripts to check them, then commit them. AiSloth then tests them in a fresh nook with only the project's files, and tells you if they fail.
        """;

    // What the agent is asked after a test failed: the failure, to fix.
    public static string FixRequest(string output)
    {
        return "AiSloth ran the project's setup in a fresh nook, with only the project's files, and it failed:\n\n```\n" + output + "\n```\n\n"
            + "Fix `.agents/setup` and `.agents/resume` so they work from scratch and when run again, run them, and commit them. AiSloth tests them again after this.";
    }

    // Follows a setup run to its end, keeping the end of its output. A watch that fails ends as a
    // failed run, saying why.
    public async Task<SetupEnd> FollowAsync(NookId nookId, ProcessId run, CancellationToken ct)
    {
        try
        {
            return await ReadAsync(nookId, run, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            return new SetupEnd(ProcessExited.Lost, "Following the setup failed: " + exception.Message);
        }
    }

    // Tests the setup as a new nook runs it: in a fresh nook, as the person, with only the files of
    // the chat's nook at its latest checkpoint and its harness's image, from scratch and then again.
    // The fresh nook goes afterwards, however the test ended.
    // Not handled: the control plane stopping mid-test leaves the fresh nook in the workspace, for
    // people to delete.
    public async Task<SetupTestResult> TestAsync(Actor person, NookId chatNook, string harness, CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        INooksApi nooks = scope.ServiceProvider.GetRequiredService<INooksApi>();
        Result<NookId> created = await CreateFreshAsync(nooks, person, chatNook, harness, ct);
        if (created.Failed)
        {
            return SetupTestResult.CouldNotRun("The fresh nook couldn't be created: " + created.Error.Message);
        }

        try
        {
            return await RunTwiceAsync(nooks, person, created.Output, ct);
        }
        finally
        {
            Result deleted = await nooks.DeleteAsync(person, created.Output, CancellationToken.None);
            if (deleted.Failed)
            {
                Log.TestNookKept(logger, created.Output.Value, deleted.Error.Message);
            }
        }
    }

    private static async Task<Result<NookId>> CreateFreshAsync(INooksApi nooks, Actor person, NookId chatNook, string harness, CancellationToken ct)
    {
        Result<NookSummary> source = await nooks.GetAsync(person, chatNook, ct);
        if (source.Failed)
        {
            return new Result<NookId>(source.Error);
        }

        Result<Page<CheckpointSummary>> checkpoints = await nooks.ListCheckpointsAsync(person, chatNook, new PageRequest(cursor: null, limit: 1), ct);
        int? latest = checkpoints.Failed || checkpoints.Output.Items.Count == 0 ? null : checkpoints.Output.Items[0].Number;
        CreateNook fresh = new CreateNook(source.Output.WorkspaceId, source.Output.Provider, harness, [], CopyOf: chatNook, Checkpoint: latest, KeptPaths: []);
        Result<NookSummary> created = await nooks.CreateAsync(person, fresh, ct);
        return created.Failed ? new Result<NookId>(created.Error) : new Result<NookId>(created.Output.Id);
    }

    // The first run starts when the nook gets its files; the second when it is asked to run again.
    private async Task<SetupTestResult> RunTwiceAsync(INooksApi nooks, Actor person, NookId nookId, CancellationToken ct)
    {
        Result<NookSetup> first = await FirstRunAsync(nooks, person, nookId, ct);
        if (first.Failed)
        {
            return SetupTestResult.CouldNotRun("The fresh nook didn't start: " + first.Error.Message);
        }

        if (first.Output.Run is not SetupRun firstRun)
        {
            return SetupTestResult.CouldNotRun("The project has no .agents/setup or .agents/resume scripts, in /work or a folder directly in it.");
        }

        (SetupEnd firstEnd, TimeSpan fromScratch) = await EndOfAsync(nooks, person, nookId, firstRun, ct);
        if (firstEnd.ExitCode != 0)
        {
            return new SetupTestResult(firstEnd.ExitCode, fromScratch, Again: null, firstEnd.Output);
        }

        Result<NookSetup> second = await nooks.RunSetupAsync(person, nookId, ct);
        if (second.Failed || second.Output.Run is not SetupRun secondRun)
        {
            string why = second.Failed ? second.Error.Message : "it found no scripts";
            return new SetupTestResult(ProcessExited.Lost, fromScratch, Again: null, "Running the setup again couldn't start: " + why);
        }

        (SetupEnd secondEnd, TimeSpan again) = await EndOfAsync(nooks, person, nookId, secondRun, ct);
        string? output = secondEnd.ExitCode == 0 ? null : "It worked from scratch, but failed when run again:\n" + secondEnd.Output;
        return new SetupTestResult(secondEnd.ExitCode, fromScratch, again, output);
    }

    // The nook's setup once it has started, waiting while the fresh nook starts.
    private static async Task<Result<NookSetup>> FirstRunAsync(INooksApi nooks, Actor person, NookId nookId, CancellationToken ct)
    {
        using CancellationTokenSource patience = CancellationTokenSource.CreateLinkedTokenSource(ct);
        patience.CancelAfter(StartPatience);
        Result<NookSetup> setup = new Result<NookSetup>(NooksErrors.NotReady);
        while (setup.Failed && setup.Error == NooksErrors.NotReady && !patience.IsCancellationRequested)
        {
            setup = await nooks.GetSetupAsync(person, nookId, patience.Token);
        }

        return setup;
    }

    // How a run ended, and how long it took.
    private async Task<(SetupEnd Ended, TimeSpan Took)> EndOfAsync(INooksApi nooks, Actor person, NookId nookId, SetupRun run, CancellationToken ct)
    {
        SetupEnd ended = await FollowAsync(nookId, run.Process, ct);
        Result<NookSetup> after = await nooks.GetSetupAsync(person, nookId, ct);
        DateTimeOffset endedAt = !after.Failed && after.Output.Run is { EndedAt: DateTimeOffset at } ? at : time.GetUtcNow();
        return (ended, endedAt - run.StartedAt);
    }

    private async Task<SetupEnd> ReadAsync(NookId nookId, ProcessId run, CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Result<IAsyncEnumerable<ProcessEvent>> watch = await scope.ServiceProvider.GetRequiredService<INooksApi>()
            .WatchProcessAsync(SystemActors.Harness, new WatchProcess(nookId, run, FromOffset: 0), ct);
        if (watch.Failed)
        {
            return new SetupEnd(ProcessExited.Lost, "Following the setup failed: " + watch.Error.Message);
        }

        List<byte> kept = [];
        await foreach (ProcessEvent processEvent in watch.Output.WithCancellation(ct))
        {
            switch (processEvent.Value)
            {
                case ProcessOutput printed:
                    kept.AddRange(printed.Data.Span);
                    kept.RemoveRange(0, Math.Max(0, kept.Count - (MaxOutput * 4)));
                    break;
                case ProcessExited exited:
                    return new SetupEnd(exited.ExitCode, Tail(Encoding.UTF8.GetString([.. kept])));
            }
        }

        return new SetupEnd(ProcessExited.Lost, "The setup's output ended before it did.");
    }

    // The end of a setup's output, from the start of a line.
    private static string Tail(string output)
    {
        string trimmed = output.TrimEnd();
        if (trimmed.Length <= MaxOutput)
        {
            return trimmed;
        }

        string end = trimmed[^MaxOutput..];
        int line = end.IndexOf('\n', StringComparison.Ordinal);
        return line < 0 ? end : end[(line + 1)..];
    }
}
