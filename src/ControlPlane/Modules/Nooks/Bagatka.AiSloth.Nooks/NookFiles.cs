using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Data;
using Bagatka.AiSloth.Nooks.Jobs;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

// A nook's files before anything runs in it (README, "Sources go in before anything runs" and
// "Setup"): its repositories copied in with its creator's GitHub connection, or a checkpoint's files
// put back; then its setup, found in the files and started; and the ready copy a setup that took a
// while earns, taken before anything else touches the nook.
internal sealed class NookFiles(
    NookProcesses processes,
    Checkpoints checkpoints,
    NookLifecycle lifecycle,
    ISourcesApi sources,
    ReadyCopies readyCopies,
    FileLocks fileLocks)
{
    // The list of a nook's setup scripts: their paths, one a line.
    private const long MaxSetupList = 1024 * 1024;

    // Puts the nook's files in place, then starts its setup; a nook that woke with its files runs
    // only its resume scripts. Done once, by whichever call comes first while the others wait; a
    // failure is returned, and the next call tries again.
    public async Task<Result> PrepareAsync(NooksDbContext db, Nook nook, DaemonConnection connection, CancellationToken ct)
    {
        if (nook.SourcesReady && !nook.ResumeDue)
        {
            return new Result(new Success());
        }

        using IDisposable held = await fileLocks.AcquireAsync(nook.Id, ct);
        await db.Entry(nook).ReloadAsync(ct);
        if (nook.SourcesReady && !nook.ResumeDue)
        {
            return new Result(new Success());
        }

        if (nook.SourcesReady)
        {
            return await ResumeAsync(db, nook, connection, ct);
        }

        List<SourceCopy> copies = await db.SourceCopies.Where(copy => copy.NookId == nook.Id).OrderBy(copy => copy.Name).ToListAsync(ct);
        int? copiedCheckpoint = null;
        if (nook.CopyOf is NookId source)
        {
            Result<int> copied = await CopyCheckpointAsync(db, nook, source, connection, copies, ct);
            if (copied.Failed)
            {
                return new Result(copied.Error);
            }

            copiedCheckpoint = copied.Output;
        }
        else
        {
            foreach (SourceCopy copy in copies.Where(copy => !copy.CopiedIn))
            {
                Result cloned = await CloneAsync(db, nook, copy, connection, ct);
                if (cloned.Failed)
                {
                    return cloned;
                }
            }
        }

        // A nook that got no files, without repositories or a checkpoint, has no setup to look for.
        Result<SetupStart> setup = new Result<SetupStart>(new SetupStart([], Process: null));
        if (copies.Count > 0 || copiedCheckpoint is not null)
        {
            setup = await StartSetupAsync(nook, connection, resumeOnly: false, ct);
        }

        if (setup.Failed)
        {
            return new Result(setup.Error);
        }

        return await MarkPreparedAsync(db, nook, copiedCheckpoint, setup.Output, ct);
    }

    // A nook that woke with its files runs only its resume scripts.
    private async Task<Result> ResumeAsync(NooksDbContext db, Nook nook, DaemonConnection connection, CancellationToken ct)
    {
        Result<SetupStart> resumed = await StartSetupAsync(nook, connection, resumeOnly: true, ct);
        if (resumed.Failed)
        {
            return new Result(resumed.Error);
        }

        return await MarkPreparedAsync(db, nook, copiedCheckpoint: null, resumed.Output, ct);
    }

    // Finds the setup scripts in the nook's files, just put in place, and starts them with the
    // workspace's secrets, without waiting for them; only the resume scripts for a nook that woke.
    // Returns the scripts, and the process running them; none when there are no scripts.
    public async Task<Result<SetupStart>> StartSetupAsync(Nook nook, DaemonConnection connection, bool resumeOnly, CancellationToken ct)
    {
        using MemoryStream found = new MemoryStream();
        ProcessRun listed = await processes.RunAsync(connection, Scripts.FindSetup, [], NookProcesses.NoVariables, input: null, ScriptOutput.Small(found, MaxSetupList), ct);
        if (!listed.Succeeded)
        {
            return new Result<SetupStart>(SourcesFailed("Looking for the nook's setup failed: " + listed.Errors));
        }

        // Not handled: folders whose names hold a line break, which checkpoints refuse too.
        List<string> scripts = [.. Encoding.UTF8.GetString(found.ToArray()).Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Where(script => !resumeOnly || script.EndsWith("/resume", StringComparison.Ordinal))];
        if (scripts.Count == 0)
        {
            return new Result<SetupStart>(new SetupStart(scripts, Process: null));
        }

        IReadOnlyDictionary<string, string> environment = await processes.EnvironmentAsync(nook, NookProcesses.NoVariables, ct);
        StartProcess command = new StartProcess(nook.Id, "/bin/bash", ["-c", Scripts.RunSetup, "setup", .. scripts], "/work", OutputRetention.Recent, environment);
        Process process = await processes.RecordAsync(command, ct);
        bool sent = await connection.SendAsync(new DaemonInstruction(process.ToInstruction(environment, inputStreamed: false)), ct);
        if (!sent)
        {
            return new Result<SetupStart>(SourcesFailed("The nook's daemon disconnected before its setup could start; try again."));
        }

        return new Result<SetupStart>(new SetupStart(scripts, process.Id));
    }

    // Runs the nook's setup again, as its scripts are now; one run at a time, because runs share the
    // log and a second would race the first.
    public async Task<Result> RunSetupAgainAsync(NooksDbContext db, Nook nook, DaemonConnection connection, CancellationToken ct)
    {
        using IDisposable held = await fileLocks.AcquireAsync(nook.Id, ct);
        await db.Entry(nook).ReloadAsync(ct);
        bool running = false;
        if (nook.SetupProcessId is ProcessId run)
        {
            running = await db.Processes.AnyAsync(process => process.Id == run && process.ExitCode == null, ct);
        }

        if (running)
        {
            return new Result(NooksErrors.SetupRunning);
        }

        Result<SetupStart> started = await StartSetupAsync(nook, connection, resumeOnly: false, ct);
        if (started.Failed)
        {
            return new Result(started.Error);
        }

        return await MarkPreparedAsync(db, nook, copiedCheckpoint: null, started.Output, ct);
    }

    // Its files are in place with this setup run. A copy records the checkpoint it came from.
    public static async Task<Result> MarkPreparedAsync(NooksDbContext db, Nook nook, int? copiedCheckpoint, SetupStart setup, CancellationToken ct)
    {
        if (copiedCheckpoint is int number)
        {
            nook.Copies(number);
        }

        nook.SourcesPrepared(setup.Scripts, setup.Process);
        return await db.SaveAsync(ct);
    }

    // Before anything touches a nook after its setup started: once the setup ended, takes the nook's
    // ready copy if the setup earned one, by succeeding after a while. Something touching the nook while
    // its setup runs would leave more than the setup's work in a copy, so then there is none.
    public async Task KeepReadyCopyAsync(NooksDbContext db, Nook nook, CancellationToken ct)
    {
        if (!nook.ReadyCopyDue)
        {
            return;
        }

        using IDisposable held = await fileLocks.AcquireAsync(nook.Id, ct);
        await db.Entry(nook).ReloadAsync(ct);
        if (!nook.ReadyCopyDue)
        {
            return;
        }

        Process? setup = await db.Processes.AsNoTracking().SingleOrDefaultAsync(process => process.Id == nook.SetupProcessId, ct);
        bool earned = setup is { ExitCode: 0, ExitedAt: DateTimeOffset ended } && ended - setup.StartedAt >= ReadyCopy.WorthKeeping;
        if (earned)
        {
            await readyCopies.TakeAsync(nook, ct);
        }

        nook.ReadyCopyHandled();
        await db.SaveAsync(ct);
    }

    // Copies one repository in with the creator's GitHub connection, and sets who their commits name.
    // Not handled: a copy that hangs, such as one whose daemon never reads its input, holds the nook's
    // first process until its caller gives up; a deadline per copy would end it.
    private async Task<Result> CloneAsync(NooksDbContext db, Nook nook, SourceCopy copy, DaemonConnection connection, CancellationToken ct)
    {
        if (nook.CreatedBy is not UserId creator)
        {
            return new Result(SourcesFailed("Nobody's GitHub connection can copy " + copy.Name + " in: the control plane created this nook."));
        }

        Actor person = Actor.ForUser(creator);
        Result<ExportedRepository> exported = new Result<ExportedRepository>(SourcesFailed("The nook's daemon never read the copy."));
        ProcessRun run = await processes.RunAsync(
            connection,
            Scripts.Clone,
            [copy.Name],
            NookProcesses.NoVariables,
            async (stream, token) => { exported = await sources.ExportAsync(person, new ExportRepository(copy.RepositoryId, copy.Branch), stream, token); },
            output: null,
            ct);
        if (exported.Failed)
        {
            return new Result(SourcesFailed("Copying " + copy.Name + " into the nook failed: " + exported.Error.Message));
        }

        if (!run.Succeeded)
        {
            return new Result(SourcesFailed("Copying " + copy.Name + " into the nook failed: " + run.Errors));
        }

        Result configured = await ConfigureGitAsync(creator, copy.Name, exported.Output.Url.AbsoluteUri, connection, ct);
        if (configured.Failed)
        {
            return configured;
        }

        copy.Copied(exported.Output.Branch, exported.Output.Commit);
        return await db.SaveAsync(ct);
    }

    // Sets who commits in the source name the person, as their git settings say, and points origin
    // at the URL unless it is empty.
    private async Task<Result> ConfigureGitAsync(UserId person, string name, string origin, DaemonConnection connection, CancellationToken ct)
    {
        Result<GitSettings> gitSettings = await sources.GetGitSettingsAsync(Actor.ForUser(person), ct);
        if (gitSettings.Failed || gitSettings.Output.Effective is not CommitIdentity identity)
        {
            return new Result(SourcesFailed("Who commits in " + name + " name isn't known: connect GitHub, or set a git author."));
        }

        string[] configuration = [name, origin, identity.Author.Name, identity.Author.Email, identity.Committer.Name, identity.Committer.Email, identity.CoAuthor ?? string.Empty];
        ProcessRun configured = await processes.RunAsync(connection, Scripts.ConfigureGit, configuration, NookProcesses.NoVariables, input: null, output: null, ct);
        return configured.Succeeded
            ? new Result(new Success())
            : new Result(SourcesFailed("Setting up git in " + name + " failed: " + configured.Errors));
    }

    // Puts a checkpoint's files in place: the nook's own latest one, after its sandbox was lost, or
    // one of the nook it copies, taken now unless one was chosen. A copy takes the other nook's
    // repositories with it, but never its kept paths, which belong to that nook's agent. Git's
    // settings aren't in checkpoints, so who commits is set again, as the nook's creator. Returns the
    // checkpoint copied.
    private async Task<Result<int>> CopyCheckpointAsync(NooksDbContext db, Nook nook, NookId from, DaemonConnection connection, List<SourceCopy> copies, CancellationToken ct)
    {
        int number;
        if (nook.CopyCheckpoint is int chosen)
        {
            number = chosen;
        }
        else
        {
            Result<Checkpoint> taken = await CheckpointToCopyAsync(db, from, ct);
            if (taken.Failed)
            {
                return new Result<int>(taken.Error);
            }

            number = taken.Output.Number;
        }

        List<KeptPlace>? places = await Checkpoints.PlacesAsync(db, from, number, ct);
        if (places is null)
        {
            return new Result<int>(SourcesFailed("The checkpoint to start from is gone."));
        }

        bool own = from == nook.Id;
        places = own ? places : [.. places.Where(place => place.Path is not "/")];
        ProcessRun restored = await checkpoints.RestoreAsync(connection, places, archive: string.Empty, output: null, ct);
        if (!restored.Succeeded)
        {
            return new Result<int>(SourcesFailed("Putting the checkpoint's files in the nook failed: " + restored.Errors));
        }

        if (!own && copies.Count == 0)
        {
            List<SourceCopy> theirs = await db.SourceCopies.AsNoTracking().Where(copy => copy.NookId == from).ToListAsync(ct);
            copies.AddRange(theirs.Select(copy => copy.CopyTo(nook.Id)));
            db.SourceCopies.AddRange(copies);
        }

        // A nook the control plane created has nobody to commit as.
        if (nook.CreatedBy is UserId creator)
        {
            foreach (SourceCopy copy in copies.Where(copy => places.Exists(place => string.Equals(place.Path, "/work/" + copy.Name, StringComparison.Ordinal))))
            {
                Result configured = await ConfigureGitAsync(creator, copy.Name, origin: string.Empty, connection, ct);
                if (configured.Failed)
                {
                    return new Result<int>(configured.Error);
                }
            }
        }

        Result saved = await db.SaveAsync(ct);
        return saved.Failed ? new Result<int>(saved.Error) : new Result<int>(number);
    }

    // A checkpoint of the nook to copy, as it is now, with its own sources in place first.
    private async Task<Result<Checkpoint>> CheckpointToCopyAsync(NooksDbContext db, NookId sourceId, CancellationToken ct)
    {
        Nook? source = await db.Nooks.SingleOrDefaultAsync(found => found.Id == sourceId, ct);
        if (source is null)
        {
            return new Result<Checkpoint>(SourcesFailed("The nook to copy is gone."));
        }

        Result<DaemonConnection> connected = await lifecycle.ConnectAsync(db, SystemActors.Processes, sourceId, ct);
        if (connected.Failed)
        {
            return new Result<Checkpoint>(SourcesFailed("The nook to copy can't be reached: " + connected.Error.Message));
        }

        DaemonConnection sourceConnection = connected.Output;

        Result sourceReady = await PrepareAsync(db, source, sourceConnection, ct);
        if (sourceReady.Failed)
        {
            return new Result<Checkpoint>(sourceReady.Error);
        }

        return await checkpoints.SaveAsync(db, source, sourceConnection, "Copied into a new nook", onlyIfChanged: false, ct);
    }

    private static Error SourcesFailed(string why)
    {
        return Error.Conflict("nooks.sources_failed", why);
    }
}
