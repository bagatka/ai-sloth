using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Machines.Model;
using Bagatka.Foundation;
using Bagatka.Sandboxing.Remote;
using Bagatka.Sandboxing.Remote.V1;
using Microsoft.EntityFrameworkCore;
using Error = Bagatka.Foundation.Error;

namespace Bagatka.AiSloth.Machines;

internal sealed partial class MachinesApi
{
    public async Task<Result<IAsyncEnumerable<SandboxCall>>> ConnectAsync(Actor actor, ConnectMachine command, IAsyncEnumerable<SandboxCallResult> results, CancellationToken ct)
    {
        // A machine proves itself with its token; the actor is always anonymous.
        Machine? machine = await db.Machines.AsNoTracking().SingleOrDefaultAsync(found => found.Id == command.MachineId, ct);
        if (machine is null || !machine.AcceptsToken(command.Token))
        {
            return new Result<IAsyncEnumerable<SandboxCall>>(Error.Unauthorized);
        }

        return new Result<IAsyncEnumerable<SandboxCall>>(RelayAsync(machine.Id, results, ct));
    }

    // The connection lasts until the caller stops reading calls or the machine stops sending results.
    private async IAsyncEnumerable<SandboxCall> RelayAsync(MachineId machineId, IAsyncEnumerable<SandboxCallResult> results, [EnumeratorCancellation] CancellationToken ct)
    {
        RemoteSandboxProvider connection = connections.Connect(machineId);
        using CancellationTokenSource ending = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task receiving = ReceiveAsync(machineId, connection, results, ending.Token);
        try
        {
            await foreach (SandboxCall call in connection.Calls.ReadAllAsync(ct))
            {
                yield return call;
            }
        }
        finally
        {
            connections.Disconnect(machineId, connection);
            await ending.CancelAsync();
            await receiving;
        }
    }

    // A machine that stops sending results can't answer the calls in flight, so the connection ends.
    private async Task ReceiveAsync(MachineId machineId, RemoteSandboxProvider connection, IAsyncEnumerable<SandboxCallResult> results, CancellationToken ct)
    {
        try
        {
            await foreach (SandboxCallResult result in results.WithCancellation(ct))
            {
                connection.Complete(result);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The relay ended first.
        }
        finally
        {
            connections.Disconnect(machineId, connection);
        }
    }
}
