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

    // Points origin at GitHub, without credentials, and sets who commits name. The hook credits the
    // co-author in aisloth.coauthor on every commit, when there is one.
    private const string ConfigureScript = """
        set -eu
        cd "/work/$1"
        git remote set-url origin "$2"
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

    private const string CopyOutScript = "tar -czf - -C /work .";

    private const string CopyInScript = "set -eu; mkdir -p /work; tar -xzf - -C /work";

    // Writes the agents' guide on standard input as /work/AGENTS.md, and points CLAUDE.md at it.
    private const string GuideScript = "set -eu; cat > /work/AGENTS.md; [ -e /work/CLAUDE.md ] || printf '@AGENTS.md\\n' > /work/CLAUDE.md";

    // Puts the nook's sources in place before anything else runs in it: its repositories, copied in
    // with its creator's GitHub connection, or a copy of another nook's files; then the guide that
    // tells agents where they are. Done once, by whichever call comes first while the others wait; a
    // failure is returned, and the next call tries again.
    private async Task<Result> PrepareSourcesAsync(Nook nook, DaemonConnection connection, CancellationToken ct)
    {
        if (nook.SourcesReady)
        {
            return new Result(new Success());
        }

        using IDisposable held = await sourceLocks.AcquireAsync(nook.Id, ct);
        await db.Entry(nook).ReloadAsync(ct);
        if (nook.SourcesReady)
        {
            return new Result(new Success());
        }

        List<SourceCopy> copies = await db.SourceCopies.Where(copy => copy.NookId == nook.Id).OrderBy(copy => copy.Name).ToListAsync(ct);
        Result copied;
        if (nook.CopyOf is NookId source)
        {
            copied = await CopyNookAsync(nook, source, connection, copies, ct);
        }
        else
        {
            copied = await CloneAllAsync(nook, copies, connection, ct);
        }

        if (copied.Failed)
        {
            return copied;
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

        return await MarkPreparedAsync(nook, ct);
    }

    // The daemon's disk reports change the nook while its sources are copied in, so marking it ready
    // reads it again and retries on their conflict.
    private async Task<Result> MarkPreparedAsync(Nook nook, CancellationToken ct)
    {
        Result saved = new Result(ModuleDbContextExtensions.ConcurrencyConflict);
        for (int attempt = 0; attempt < 3 && saved.Failed && saved.Error == ModuleDbContextExtensions.ConcurrencyConflict; attempt++)
        {
            await db.Entry(nook).ReloadAsync(ct);
            nook.SourcesPrepared();
            saved = await db.SaveAsync(ct);
        }

        return saved;
    }

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

        Result<GitSettings> gitSettings = await sources.GetGitSettingsAsync(person, ct);
        if (gitSettings.Failed || gitSettings.Output.Effective is not CommitIdentity identity)
        {
            return new Result(SourcesFailed("Who commits in " + copy.Name + " name isn't known: connect GitHub, or set a git author."));
        }

        string[] configuration = [copy.Name, exported.Output.Url.AbsoluteUri, identity.Author.Name, identity.Author.Email, identity.Committer.Name, identity.Committer.Email, identity.CoAuthor ?? string.Empty];
        ProcessRun configured = await RunAsync(connection, ConfigureScript, configuration, NoVariables, input: null, output: null, ct);
        if (!configured.Succeeded)
        {
            return new Result(SourcesFailed("Setting up git in " + copy.Name + " failed: " + configured.Errors));
        }

        copy.Copied(exported.Output.Branch, exported.Output.Commit);
        return await db.SaveAsync(ct);
    }

    // Copies another nook's /work in, as it is now, with its repositories' records.
    private async Task<Result> CopyNookAsync(Nook nook, NookId sourceId, DaemonConnection connection, List<SourceCopy> copies, CancellationToken ct)
    {
        Nook? source = await db.Nooks.SingleOrDefaultAsync(found => found.Id == sourceId, ct);
        DaemonConnection? sourceConnection = null;
        if (source is not null)
        {
            sourceConnection = await ConnectionAsync(sourceId, ct);
        }

        if (source is null || sourceConnection is null)
        {
            return new Result(SourcesFailed("The nook to copy is gone, or isn't running."));
        }

        Result sourceReady = await PrepareSourcesAsync(source, sourceConnection, ct);
        if (sourceReady.Failed)
        {
            return sourceReady;
        }

        ProcessRun? copiedOut = null;
        ProcessRun copiedIn = await RunAsync(
            connection,
            CopyInScript,
            [],
            NoVariables,
            async (stream, token) => { copiedOut = await RunAsync(sourceConnection, CopyOutScript, [], NoVariables, input: null, stream, token); },
            output: null,
            ct);
        if (copiedOut is not { Succeeded: true } || !copiedIn.Succeeded)
        {
            return new Result(SourcesFailed("Copying the other nook's files failed: " + (copiedOut?.Errors ?? string.Empty) + " " + copiedIn.Errors));
        }

        List<SourceCopy> theirs = await db.SourceCopies.AsNoTracking().Where(copy => copy.NookId == sourceId).ToListAsync(ct);
        foreach (SourceCopy copy in theirs)
        {
            SourceCopy ours = copy.CopyTo(nook.Id);
            db.SourceCopies.Add(ours);
            copies.Add(ours);
        }

        return await db.SaveAsync(ct);
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
