using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Wire = Bagatka.Sandboxing.Remote.V1;

namespace Bagatka.AiSloth.Machines.Contracts;

/// <summary>
/// The machine side of machines, called only by the WebApi's machine endpoint when <c>sloth machine</c>
/// dials in. It is never a public route or a tool. A machine proves itself with its code, then its
/// token, so the actor is always <see cref="Actor.Anonymous"/>.
/// </summary>
public interface IMachineConnectionsApi
{
    /// <summary>Trades a registration code for the machine's credential. A code works once, within its hour.</summary>
    /// <returns>The credential; or unauthorized for an unknown, used, or expired code.</returns>
    public Task<Result<MachineCredential>> RegisterAsync(Actor actor, RegisterMachine command, CancellationToken ct);

    /// <summary>
    /// A machine's connection: sandbox provider calls flow out, and their results flow in, in any order,
    /// until either side ends it or <paramref name="ct"/> is cancelled. A newer connection for the same
    /// machine replaces an older one.
    /// </summary>
    /// <returns>The calls to run; or unauthorized when the token doesn't match the machine.</returns>
    public Task<Result<IAsyncEnumerable<Wire.SandboxCall>>> ConnectAsync(
        Actor actor,
        ConnectMachine command,
        IAsyncEnumerable<Wire.SandboxCallResult> results,
        CancellationToken ct);
}
