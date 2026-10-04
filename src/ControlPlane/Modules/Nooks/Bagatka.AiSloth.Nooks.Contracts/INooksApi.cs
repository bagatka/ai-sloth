using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Workspaces.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// Nooks: where agents work. A nook is an isolated machine with its files and processes, owned by a
/// workspace; any member may use it.
/// </summary>
/// <remarks>
/// Suspension is invisible to callers: an operation on a paused or stopped nook resumes it first, so
/// callers only notice latency. A nook's files survive until it is deleted.
/// </remarks>
public interface INooksApi
{
    /// <summary>
    /// Records a nook in the workspace, then asks the chosen sandbox provider to create it. Returns
    /// as soon as it is recorded; a failed provider call is retried by reconciliation, not by the caller.
    /// </summary>
    /// <returns>
    /// The nook in <see cref="NookStatus.Creating"/>; not found when the actor isn't a member of the
    /// workspace; or a validation error for an unknown provider.
    /// </returns>
    public Task<Result<NookSummary>> CreateAsync(Actor actor, CreateNook command, CancellationToken ct);

    /// <summary>
    /// The nook. Not found when it doesn't exist or the actor isn't a member of its workspace.
    /// </summary>
    public Task<Result<NookSummary>> GetAsync(Actor actor, NookId id, CancellationToken ct);

    /// <summary>
    /// The workspace's nooks, newest first. Not found when the actor isn't a member.
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
}
