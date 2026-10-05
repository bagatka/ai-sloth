using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Nooks.Daemons;
using Bagatka.AiSloth.Nooks.Model;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Nooks;

internal sealed partial class NooksApi
{
    // Clones a repository from the bundle on standard input into /work/<name>, all or nothing: into a
    // folder beside it first, so a broken copy never looks like a finished one.
    private const string CloneScript = """
        set -eu
        incoming="/work/.aisloth-incoming-$1"
        rm -rf "$incoming" "$incoming.bundle"
        mkdir -p /work
        cat > "$incoming.bundle"
        branch=$(git bundle list-heads "$incoming.bundle" | sed -n 's#^[0-9a-f]* refs/heads/##p' | head -n 1)
        git clone --quiet --branch "$branch" "$incoming.bundle" "$incoming"
        rm -f "$incoming.bundle"
        rm -rf "/work/$1"
        mv "$incoming" "/work/$1"
        """;

    // Points origin at GitHub, without credentials, unless the URL is empty, and sets who commits
    // name. The hook credits the co-author in aisloth.coauthor on every commit, when there is one.
    private const string ConfigureScript = """
        set -eu
        cd "/work/$1"
        if [ -n "$2" ]; then git remote set-url origin "$2"; fi
        git config user.name "$3" && git config user.email "$4"
        git config author.name "$3" && git config author.email "$4"
        git config committer.name "$5" && git config committer.email "$6"
        git config aisloth.coauthor "$7"
        hook="$(git rev-parse --git-path hooks)/prepare-commit-msg"
        mkdir -p "$(dirname "$hook")"
        cat > "$hook" <<'HOOK'
        #!/bin/sh
        trailer=$(git config aisloth.coauthor) || exit 0
        [ -n "$trailer" ] || exit 0
        exec git interpret-trailers --in-place --if-exists addIfDifferent --trailer "$trailer" "$1"
        HOOK
        chmod +x "$hook"
        """;

    // Writes the agents' guide on standard input as /work/AGENTS.md, and points CLAUDE.md at it.
    private const string GuideScript = "set -eu; cat > /work/AGENTS.md; [ -e /work/CLAUDE.md ] || printf '@AGENTS.md\\n' > /work/CLAUDE.md";

    // Puts the nook's sources in place before anything else runs in it: its repositories, copied in
    // with its creator's GitHub connection, with the guide that tells agents where they are; or a
    // checkpoint's files. Then starts its setup. Done once, by whichever call comes first
    // while the others wait; a failure is returned, and the next call tries again.
    private async Task<Result> PrepareSourcesAsync(Nook nook, DaemonConnection connection, CancellationToken ct)
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

        // A nook that woke with its files runs only its resume scripts.
        if (nook.SourcesReady)
        {
            Result<SetupStart> resumed = await StartSetupAsync(nook, connection, resumeOnly: true, ct);
            if (resumed.Failed)
            {
                return new Result(resumed.Error);
            }

            return await MarkPreparedAsync(nook, copiedCheckpoint: null, resumed.Output, ct);
        }

        List<SourceCopy> copies = await db.SourceCopies.Where(copy => copy.NookId == nook.Id).OrderBy(copy => copy.Name).ToListAsync(ct);
        int? copiedCheckpoint = null;
        if (nook.CopyOf is NookId source)
        {
            Result<int> copied = await CopyCheckpointAsync(nook, source, connection, copies, ct);
            if (copied.Failed)
            {
                return new Result(copied.Error);
            }

            copiedCheckpoint = copied.Output;
        }
        else
        {
            Result cloned = await CloneAllAsync(nook, copies, connection, ct);
            if (cloned.Failed)
            {
                return cloned;
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

        return await MarkPreparedAsync(nook, copiedCheckpoint, setup.Output, ct);
    }

    // The daemon's disk reports change the nook while its sources are copied in, so marking it ready
    // reads it again and retries on their conflict. A copy records the checkpoint it came from.
    private async Task<Result> MarkPreparedAsync(Nook nook, int? copiedCheckpoint, SetupStart setup, CancellationToken ct)
    {
        Result saved = new Result(ModuleDbContextExtensions.ConcurrencyConflict);
        for (int attempt = 0; attempt < 3 && saved.Failed && saved.Error == ModuleDbContextExtensions.ConcurrencyConflict; attempt++)
        {
            await db.Entry(nook).ReloadAsync(ct);
            if (copiedCheckpoint is int number)
            {
                nook.Copies(number);
            }

            nook.SourcesPrepared(setup.Scripts, setup.Process);
            saved = await db.SaveAsync(ct);
        }

        return saved;
    }

    // Copies in the repositories not in place yet, then writes the guide to all of them.
    private async Task<Result> CloneAllAsync(Nook nook, List<SourceCopy> copies, DaemonConnection connection, CancellationToken ct)
    {
        foreach (SourceCopy copy in copies.Where(copy => !copy.CopiedIn))
        {
            Result cloned = await CloneAsync(nook, copy, connection, ct);
            if (cloned.Failed)
            {
                return cloned;
            }
        }

        if (copies.Count > 0)
        {
            byte[] guide = Encoding.UTF8.GetBytes(Guide(copies));
            ProcessRun written = await RunAsync(connection, GuideScript, [], NoVariables, async (stream, token) => { await stream.WriteAsync(guide, token); }, output: null, ct);
            if (!written.Succeeded)
            {
                return new Result(SourcesFailed("Writing /work/AGENTS.md failed: " + written.Errors));
            }
        }

        return new Result(new Success());
    }

    // Copies one repository in with the creator's GitHub connection, and sets who their commits name.
    private async Task<Result> CloneAsync(Nook nook, SourceCopy copy, DaemonConnection connection, CancellationToken ct)
    {
        if (nook.CreatedBy is not UserId creator)
        {
            return new Result(SourcesFailed("Nobody's GitHub connection can copy " + copy.Name + " in: the control plane created this nook."));
        }

        Actor person = Actor.ForUser(creator);
        Result<ExportedRepository> exported = new Result<ExportedRepository>(SourcesFailed("The nook's daemon never read the copy."));
        ProcessRun run = await RunAsync(
            connection,
            CloneScript,
            [copy.Name],
            NoVariables,
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
        ProcessRun configured = await RunAsync(connection, ConfigureScript, configuration, NoVariables, input: null, output: null, ct);
        return configured.Succeeded
            ? new Result(new Success())
            : new Result(SourcesFailed("Setting up git in " + name + " failed: " + configured.Errors));
    }

    // Puts a checkpoint's files in place: the nook's own latest one, after its sandbox was lost, or
    // one of the nook it copies, taken now unless one was chosen. A copy takes the other nook's
    // repositories with it, but never its kept paths, which belong to that nook's agent. Git's
    // settings aren't in checkpoints, so who commits is set again, as the nook's creator. Returns the
    // checkpoint copied.
    private async Task<Result<int>> CopyCheckpointAsync(Nook nook, NookId from, DaemonConnection connection, List<SourceCopy> copies, CancellationToken ct)
    {
        int number;
        if (nook.CopyCheckpoint is int chosen)
        {
            number = chosen;
        }
        else
        {
            Result<Checkpoint> taken = await CheckpointToCopyAsync(from, ct);
            if (taken.Failed)
            {
                return new Result<int>(taken.Error);
            }

            number = taken.Output.Number;
        }

        List<KeptPlace>? places = await PlacesAsync(from, number, ct);
        if (places is null)
        {
            return new Result<int>(SourcesFailed("The checkpoint to start from is gone."));
        }

        bool own = from == nook.Id;
        places = own ? places : [.. places.Where(place => place.Path is not "/")];
        ProcessRun restored = await RestoreAsync(connection, places, archive: string.Empty, output: null, ct);
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
    private async Task<Result<Checkpoint>> CheckpointToCopyAsync(NookId sourceId, CancellationToken ct)
    {
        Nook? source = await db.Nooks.SingleOrDefaultAsync(found => found.Id == sourceId, ct);
        DaemonConnection? sourceConnection = null;
        if (source is not null)
        {
            sourceConnection = await ConnectionAsync(SystemActors.Processes, sourceId, ct);
        }

        if (source is null || sourceConnection is null)
        {
            return new Result<Checkpoint>(SourcesFailed("The nook to copy is gone, or isn't running."));
        }

        Result sourceReady = await PrepareSourcesAsync(source, sourceConnection, ct);
        if (sourceReady.Failed)
        {
            return new Result<Checkpoint>(sourceReady.Error);
        }

        return await SaveCheckpointAsync(source, sourceConnection, "Copied into a new nook", onlyIfChanged: false, ct);
    }

    // What agents read first: where the repositories are, and that each has its own instructions.
    private static string Guide(IReadOnlyList<SourceCopy> copies)
    {
        StringBuilder guide = new StringBuilder();
        guide.Append("# Repositories in this folder\n\n");
        guide.Append("Each folder here is its own git repository, copied from GitHub:\n\n");
        foreach (SourceCopy copy in copies)
        {
            guide.Append("- `").Append(copy.Name).Append("/`, from its branch ").Append(copy.Branch).Append('\n');
        }

        guide.Append("\nBefore working in one, read its own AGENTS.md or CLAUDE.md, if it has one, and follow it there. ");
        guide.Append("Commit in the repository you changed. AiSloth pushes the commits to GitHub when asked, so don't push; ");
        guide.Append("this nook has no credentials for GitHub.\n");
        return guide.ToString();
    }

    private static Error SourcesFailed(string why)
    {
        return Error.Conflict("nooks.sources_failed", why);
    }
}
