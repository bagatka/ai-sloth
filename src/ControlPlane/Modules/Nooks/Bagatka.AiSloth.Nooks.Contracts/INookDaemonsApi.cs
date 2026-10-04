using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Nooks.Contracts;

/// <summary>
/// The daemon side of nooks, called only by the WebApi's daemon endpoint when <c>slothd</c> dials in.
/// It is never a public route or a tool.
/// </summary>
/// <remarks>
/// A daemon authenticates with the token this module issued for its nook, so the actor is always
/// <see cref="Actor.Anonymous"/>.
/// </remarks>
public interface INookDaemonsApi
{
    /// <summary>
    /// A daemon's control connection. Its process exits flow in; instructions flow out until either
    /// side ends the connection or <paramref name="ct"/> is cancelled. A newer connection for the same
    /// nook replaces an older one. A control-plane instance that shuts down sends
    /// <see cref="ReconnectInstruction"/> first, so the daemon moves to another instance.
    /// </summary>
    /// <returns>The instructions; or unauthorized when the token doesn't match the nook.</returns>
    public Task<Result<IAsyncEnumerable<DaemonInstruction>>> ConnectAsync(
        Actor actor,
        ConnectDaemon command,
        IAsyncEnumerable<ProcessExited> exits,
        CancellationToken ct);

    /// <summary>
    /// The output a daemon uploads for one <see cref="WatchOutputInstruction"/>. Returns when the
    /// upload ends: the process exited and all its output was delivered, or the watcher left and
    /// <paramref name="ct"/> was cancelled.
    /// </summary>
    /// <returns>Success; unauthorized for a wrong token; or not found when the watch already ended.</returns>
    public Task<Result> AcceptOutputAsync(
        Actor actor,
        OutputUpload upload,
        IAsyncEnumerable<ProcessOutput> output,
        CancellationToken ct);
}
