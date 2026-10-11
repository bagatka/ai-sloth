using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// What runs in nooks (<see cref="INooksApi"/>): processes, recorded before they start, their output
/// kept from any offset, fed and stopped. Read on the nook watches them; operating it starts, feeds, and
/// stops them. Starting one puts the nook's files in place first.
/// </summary>
public interface INookProcessesApi
{
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
    /// ends with <see cref="ProcessExited"/>. Any number of watchers may watch one process; while a
    /// person watches, the nook stays awake.
    /// Cancelling <paramref name="ct"/> ends the watch, never the process. A watch also ends, without
    /// the exit, when the control-plane instance serving it hands over, as in a deploy: watch again
    /// from the offset after the last output.
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
