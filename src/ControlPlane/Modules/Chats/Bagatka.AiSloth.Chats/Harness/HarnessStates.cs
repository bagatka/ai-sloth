using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Data;
using Bagatka.AiSloth.Chats.Model;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Harnesses;
using Bagatka.ObjectStorage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Chats.Harness;

// What each person's harnesses write for themselves to use later (HarnessProfile.StatePaths), in each
// workspace, kept in step across the chats they start there. A chat syncs at a turn's edges, while its
// agent doesn't write: when its agent starts, after each turn, and before a turn when another chat
// saved since. A sync merges what changed in its nook since the last one with what other chats saved
// meanwhile (StateFiles.Merge), saves that, and puts it back in the nook. It never leaves its
// workspace, whose work it may describe. Failures are logged, never the chat's: the agent works with
// what its nook has, and the next sync tries again.
internal sealed class HarnessStates(
    IDbContextFactory<ChatsDbContext> databases,
    IServiceScopeFactory scopes,
    IObjectStorage storage,
    TimeProvider time,
    ILogger<HarnessStates> logger)
{
    // Chats saving at the same moment merge again, a few times at most.
    private const int MaxAttempts = 3;

    // Whether another chat saved since this one last synced, or the state was forgotten.
    public async Task<bool> MovedAsync(Chat chat, CancellationToken ct)
    {
        if (HarnessProfiles.Find(chat.Harness) is not { StatePaths.Count: > 0 })
        {
            return false;
        }

        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);
        byte[]? saved = await Saved(db, chat).Select(state => state.Sha256).SingleOrDefaultAsync(ct);
        if (saved is null)
        {
            return !string.IsNullOrEmpty(chat.HarnessStateFiles);
        }

        return chat.HarnessStateFiles is null || !saved.AsSpan().SequenceEqual(StateFiles.VersionOf(chat.HarnessStateFiles));
    }

    // Returns the manifest of what the nook holds afterwards, for the chat to keep; its own when the
    // sync failed.
    public async Task<string?> SyncAsync(Chat chat, CancellationToken ct)
    {
        HarnessProfile? harness = HarnessProfiles.Find(chat.Harness);
        if (harness is null || harness.StatePaths.Count == 0)
        {
            return chat.HarnessStateFiles;
        }

        Result<StateFiles> ours = await CopyOutAsync(chat, harness, ct);
        if (ours.Failed)
        {
            Log.HarnessStateNotSynced(logger, chat.Id.Value, ours.Error.Message);
            return chat.HarnessStateFiles;
        }

        Result<StateFiles> merged = new Result<StateFiles>(ModuleDbContextExtensions.ConcurrencyConflict);
        for (int attempt = 0; attempt < MaxAttempts && merged.Failed && IsRace(merged.Error); attempt++)
        {
            merged = await MergeAndSaveAsync(chat, harness, ours.Output, ct);
        }

        if (merged.Failed)
        {
            Log.HarnessStateNotSynced(logger, chat.Id.Value, merged.Error.Message);
            return chat.HarnessStateFiles;
        }

        if (string.Equals(merged.Output.Manifest, ours.Output.Manifest, StringComparison.Ordinal))
        {
            return merged.Output.Manifest;
        }

        // The nook keeps its own files when they can't go in; the next sync brings the rest.
        Result copied = await CopyInAsync(chat, harness, merged.Output, ct);
        if (copied.Failed)
        {
            Log.HarnessStateNotSynced(logger, chat.Id.Value, copied.Error.Message);
            return ours.Output.Manifest;
        }

        return merged.Output.Manifest;
    }

    // Merges the nook's files with the saved state and saves the result as its new version, unless it
    // is the saved one. Another chat saving meanwhile is a race to merge again.
    private async Task<Result<StateFiles>> MergeAndSaveAsync(Chat chat, HarnessProfile harness, StateFiles ours, CancellationToken ct)
    {
        await using ChatsDbContext db = await databases.CreateDbContextAsync(ct);
        HarnessState? saved = await Saved(db, chat).SingleOrDefaultAsync(ct);
        StateFiles theirs = StateFiles.Empty;
        if (saved is not null)
        {
            Result<StateFiles> read = await ReadAsync(saved, harness, ct);
            if (read.Failed)
            {
                return read;
            }

            theirs = read.Output;
        }

        // A nook without any state, such as one created again after it was lost, didn't delete it: it
        // takes the saved one. Not handled: an agent deleting all of it, which comes back.
        bool fresh = ours.Count == 0 && !string.IsNullOrEmpty(chat.HarnessStateFiles);
        StateFiles merged = fresh ? theirs : StateFiles.Merge(ours, chat.HarnessStateFiles, theirs);
        bool unchanged = string.Equals(merged.Manifest, theirs.Manifest, StringComparison.Ordinal) && (saved is not null || merged.Count == 0);
        if (unchanged)
        {
            return new Result<StateFiles>(merged);
        }

        // The new version's archive goes first, then the row points at it, then the old one goes.
        byte[] version = merged.Version;
        using MemoryStream archive = await merged.ArchiveAsync(ct);
        await storage.PutAsync(HarnessState.KeyOf(chat.StartedBy, chat.WorkspaceId, chat.Harness, version), archive, ct);
        string? older = saved?.VersionFolder;
        if (saved is null)
        {
            db.HarnessStates.Add(HarnessState.Save(chat.StartedBy, chat.WorkspaceId, chat.Harness, archive.Length, version, chat.Id, time));
        }
        else
        {
            saved.Saved(archive.Length, version, chat.Id, time);
        }

        Result stored = await db.SaveAsync(ct);
        if (stored.Failed)
        {
            return new Result<StateFiles>(stored.Error);
        }

        if (older is not null)
        {
            await storage.DeleteAsync(older, ct);
        }

        return new Result<StateFiles>(merged);
    }

    // The saved state's files; a version a newer save already removed is a race.
    private async Task<Result<StateFiles>> ReadAsync(HarnessState saved, HarnessProfile harness, CancellationToken ct)
    {
        await using Stream? archive = await storage.OpenAsync(saved.ObjectKey, ct);
        if (archive is null)
        {
            return new Result<StateFiles>(ModuleDbContextExtensions.ConcurrencyConflict);
        }

        return await StateFiles.ReadAsync(archive, harness.StatePaths, ct);
    }

    private async Task<Result<StateFiles>> CopyOutAsync(Chat chat, HarnessProfile harness, CancellationToken ct)
    {
        string path = Path.GetTempFileName();
        await using FileStream archive = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None, bufferSize: 81920, FileOptions.DeleteOnClose | FileOptions.Asynchronous);
        Result copied;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
        {
            copied = await scope.ServiceProvider.GetRequiredService<INooksApi>().CopyFilesOutAsync(SystemActors.Harness, new CopyFilesOut(chat.NookId, harness.StatePaths), archive, ct);
        }

        if (copied.Failed)
        {
            return new Result<StateFiles>(copied.Error);
        }

        archive.Position = 0;
        return await StateFiles.ReadAsync(archive, harness.StatePaths, ct);
    }

    // Replaces the state paths in the nook, so files the state no longer has go too.
    private async Task<Result> CopyInAsync(Chat chat, HarnessProfile harness, StateFiles files, CancellationToken ct)
    {
        using MemoryStream archive = await files.ArchiveAsync(ct);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INooksApi>().CopyFilesInAsync(SystemActors.Harness, new CopyFilesIn(chat.NookId, harness.StatePaths), archive, ct);
    }

    private static IQueryable<HarnessState> Saved(ChatsDbContext db, Chat chat)
    {
        return db.HarnessStates.Where(state => state.PersonId == chat.StartedBy && state.WorkspaceId == chat.WorkspaceId && state.Harness == chat.Harness);
    }

    private static bool IsRace(Error error)
    {
        return error == ModuleDbContextExtensions.ConcurrencyConflict || error == ModuleDbContextExtensions.AlreadyExists;
    }
}
