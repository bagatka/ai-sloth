using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Nooks: where agents work. A nook is an isolated machine with its files and processes, owned by a
/// workspace. Access comes from Workspaces, through the workspace or given for the nook alone: Read
/// sees a nook and watches its processes, and Write creates, deletes, and runs processes. Without
/// access a nook is not found; with too little, forbidden.
/// </summary>
/// <remarks>
/// A nook nobody uses falls asleep after a while (<see cref="NookStatus"/>), which is invisible to
/// callers: any operation wakes it first, so callers only notice latency. Processes keep running
/// through a sleep where its provider keeps memory, and otherwise end with exit code -1. A nook's
/// files survive until it is deleted, except when its machine is lost or it sleeps long enough to
/// be evicted: then it starts again from its latest checkpoint, and processes that ran there end
/// with exit code -1.
/// </remarks>
public interface INooksApi
{
    /// <summary>
    /// Records a nook in the workspace, then asks the chosen sandbox provider to create it. Returns
    /// as soon as it is recorded; a failed provider call is retried by reconciliation, not by the caller.
    /// </summary>
    /// <remarks>
    /// A nook may be created on a provider that isn't available, such as a machine that is offline;
    /// it stays <see cref="NookStatus.Creating"/> until the provider is back.
    /// </remarks>
    /// <returns>
    /// The nook in <see cref="NookStatus.Creating"/>; not found when the actor has no access to the
    /// workspace, or forbidden without Write; or a validation error for a provider the workspace
    /// doesn't have, a harness this deployment doesn't offer, repositories or a nook to copy it can't
    /// start with, or kept paths it can't keep.
    /// </returns>
    public Task<Result<NookSummary>> CreateAsync(Actor actor, CreateNook command, CancellationToken ct);

    /// <summary>
    /// The providers the workspace's nooks can run on: the deployment's own, then the workspace's
    /// machines, oldest first. Not found when the actor has no access to the workspace.
    /// </summary>
    public Task<Result<IReadOnlyList<ProviderSummary>>> ListProvidersAsync(Actor actor, WorkspaceId workspaceId, CancellationToken ct);

    /// <summary>
    /// The nook. Not found when it doesn't exist or the actor has no access to it.
    /// </summary>
    public Task<Result<NookSummary>> GetAsync(Actor actor, NookId id, CancellationToken ct);

    /// <summary>
    /// The workspace's nooks, newest first. Not found when the actor has no access to the workspace,
    /// such as a guest of one of its nooks.
    /// </summary>
    public Task<Result<Page<NookSummary>>> ListAsync(Actor actor, WorkspaceId workspaceId, PageRequest page, CancellationToken ct);

    /// <summary>
    /// Marks the nook <see cref="NookStatus.Deleting"/> and asks its provider to delete it, with its
    /// files. Deleting a nook that is already being deleted succeeds.
    /// </summary>
    public Task<Result> DeleteAsync(Actor actor, NookId id, CancellationToken ct);

    /// <summary>
    /// Starts a process in the nook. It runs until it exits or is stopped, whoever watches it and
    /// whatever happens to the control plane: deploys and restarts never stop it.
    /// </summary>
    /// <returns>
    /// The process; not found; or <see cref="NooksErrors.NotReady"/> when the nook's daemon can't be
    /// reached in time.
    /// </returns>
    public Task<Result<ProcessSummary>> StartProcessAsync(Actor actor, StartProcess command, CancellationToken ct);

    /// <summary>
    /// The nook's setup (<see cref="NookSetup"/>). Its files are put in place first, which starts its
    /// setup when it has one. People who can see the nook may.
    /// </summary>
    /// <returns>The setup; <see cref="NooksErrors.NotReady"/>; or not found.</returns>
    public Task<Result<NookSetup>> GetSetupAsync(Actor actor, NookId id, CancellationToken ct);

    /// <summary>
    /// Runs the nook's setup again now, as its scripts are now, as when the nook got its files: to
    /// check that it is safe and fast to run again. People with Write may.
    /// </summary>
    /// <returns>The setup with the new run; <see cref="NooksErrors.SetupRunning"/>; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.</returns>
    public Task<Result<NookSetup>> RunSetupAsync(Actor actor, NookId id, CancellationToken ct);

    /// <summary>
    /// Wakes the nook when it sleeps, without waiting for it, and keeps it awake for a while: the sleep
    /// period, as after any use, for someone about to use it, such as a person opening its chat; or as
    /// long as asked, renewed by whatever keeps it busy, such as Chats while its agent works. People
    /// with Write may.
    /// </summary>
    /// <returns>Success; <see cref="NooksErrors.NotReady"/> when its provider can't be asked; a validation error for longer than an hour; or not found or forbidden.</returns>
    public Task<Result> WakeAsync(Actor actor, WakeNook command, CancellationToken ct);

    /// <summary>
    /// The nook's processes, newest first.
    /// </summary>
    public Task<Result<Page<ProcessSummary>>> ListProcessesAsync(Actor actor, NookId nookId, PageRequest page, CancellationToken ct);

    /// <summary>
    /// Streams a process's output from an offset, first what was already written and then live, and
    /// ends with <see cref="ProcessExited"/>. Any number of watchers may watch one process.
    /// Cancelling <paramref name="ct"/> ends the watch, never the process.
    /// </summary>
    /// <remarks>
    /// What can be replayed depends on the process's <see cref="OutputRetention"/>. Watching from an
    /// offset that is no longer kept starts at the earliest byte kept, which the first chunk's
    /// offset shows.
    /// </remarks>
    public Task<Result<IAsyncEnumerable<ProcessEvent>>> WatchProcessAsync(Actor actor, WatchProcess command, CancellationToken ct);

    /// <summary>
    /// Writes to a running process's standard input.
    /// </summary>
    public Task<Result> SendInputAsync(Actor actor, SendInput command, CancellationToken ct);

    /// <summary>
    /// Stops a process: first politely, then forcibly. Stopping a process that already exited succeeds.
    /// </summary>
    public Task<Result> StopProcessAsync(Actor actor, StopProcess command, CancellationToken ct);

    /// <summary>
    /// Writes the nook's files to <paramref name="destination"/> as a gzipped tar archive: one of its sources,
    /// or all of <c>/work</c>, as they are now or at one of its checkpoints. People who can see the nook
    /// may. Its sources are put in place first. A checkpoint's files are as <see cref="CheckpointAsync"/> keeps them.
    /// </summary>
    /// <returns>
    /// Success; <see cref="NooksErrors.SourceNotFound"/>, also for a source the checkpoint doesn't have;
    /// <see cref="NooksErrors.CheckpointNotFound"/>; <see cref="NooksErrors.NotReady"/>; or not found.
    /// </returns>
    public Task<Result> DownloadAsync(Actor actor, DownloadFiles command, Stream destination, CancellationToken ct);

    /// <summary>
    /// Saves the nook's files as its next checkpoint, kept until the nook is deleted: <c>/work</c> and
    /// every git repository directly in it, with their history and branches, honoring
    /// <c>.gitignore</c>, and the nook's kept paths (<see cref="CreateNook.KeptPaths"/>). Files a
    /// <c>.gitignore</c> leaves out, such as installed dependencies, aren't kept, nor are git's
    /// settings besides remotes. Only what changed since the last checkpoint is stored. Takes a few
    /// seconds, more the first time; files the nook changes meanwhile may be either way. People with
    /// Write may.
    /// </summary>
    /// <returns>The checkpoint; a validation error for a note too long; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.</returns>
    public Task<Result<CheckpointSummary>> CheckpointAsync(Actor actor, CheckpointNook command, CancellationToken ct);

    /// <summary>
    /// Writes the files at the paths, those that exist, to <paramref name="destination"/> as a
    /// gzipped tar archive of names relative to <c>/</c>, for <see cref="CopyFilesInAsync"/>. The same
    /// files make the same bytes: names sorted, without times or owners. People with Write may.
    /// </summary>
    /// <returns>Success; a validation error for paths that aren't absolute; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.</returns>
    public Task<Result> CopyFilesOutAsync(Actor actor, CopyFilesOut command, Stream destination, CancellationToken ct);

    /// <summary>
    /// Unpacks <paramref name="archive"/>, a gzipped tar archive such as <see cref="CopyFilesOutAsync"/>
    /// writes, into the nook at <c>/</c>, replacing the files it names, after removing the paths it
    /// replaces (<see cref="CopyFilesIn.Replacing"/>). Its sources are put in place first. People with
    /// Write may.
    /// </summary>
    /// <returns>Success; a validation error for paths that aren't absolute; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.</returns>
    public Task<Result> CopyFilesInAsync(Actor actor, CopyFilesIn command, Stream archive, CancellationToken ct);

    /// <summary>The nook's checkpoints, newest first. People who can see the nook may.</summary>
    public Task<Result<Page<CheckpointSummary>>> ListCheckpointsAsync(Actor actor, NookId nookId, PageRequest page, CancellationToken ct);

    /// <summary>
    /// Commits what a source has that isn't committed, then writes its commits since it was
    /// copied in to <paramref name="destination"/> as a git bundle, for pushing. People with
    /// Write only. A source without commits beyond its start writes nothing.
    /// </summary>
    /// <returns>
    /// The changes; <see cref="NooksErrors.SourceNotFound"/> for a source that isn't one of its
    /// repositories; <see cref="NooksErrors.NotReady"/>; or not found or forbidden.
    /// </returns>
    public Task<Result<ExportedChanges>> ExportChangesAsync(Actor actor, ExportChanges command, Stream destination, CancellationToken ct);
}
