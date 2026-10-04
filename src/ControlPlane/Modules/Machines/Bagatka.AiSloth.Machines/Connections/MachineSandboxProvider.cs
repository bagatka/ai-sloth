using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.AiSloth.Machines.Data;
using Bagatka.AiSloth.Machines.Model;
using Bagatka.Foundation;
using Bagatka.Foundation.Modules;
using Bagatka.Sandboxing;
using Bagatka.Sandboxing.Remote;
using Microsoft.EntityFrameworkCore;

namespace Bagatka.AiSloth.Machines.Connections;

// The `machine` sandbox provider: a sandbox's location is the machine it runs on, and every call goes
// over that machine's connection to the provider it runs locally. Where each sandbox and snapshot
// lives is recorded, so calls by key find it. A machine that isn't connected makes calls for its
// sandboxes throw, as an unreachable backend does, and callers retry.
internal sealed class MachineSandboxProvider(MachineConnections connections, IDbContextFactory<MachinesDbContext> databases) : ISandboxProvider
{
    private static readonly Error SandboxNotFound =
        Error.NotFound("sandboxing.sandbox_not_found", "The sandbox doesn't exist.");

    public string Name => MachineProvider.Name;

    public async Task<Result<SandboxObservation>> CreateAsync(SandboxSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (!MachineProvider.TryParseLocation(spec.Location, out MachineId machine))
        {
            return new Result<SandboxObservation>(Error.Validation("location", "The location must be the ID of a machine."));
        }

        await using MachinesDbContext db = await databases.CreateDbContextAsync(ct);
        if (!await db.Machines.AnyAsync(found => found.Id == machine, ct))
        {
            return new Result<SandboxObservation>(Error.Validation("location", "No machine has this ID."));
        }

        // A snapshot's files are on one machine, so sandboxes made from it start there too.
        if (spec.Source.Value is SnapshotKey snapshot && await PlacementAsync(db, snapshot.Value, ct) != machine)
        {
            return new Result<SandboxObservation>(Error.Validation("source", "The snapshot is on another machine."));
        }

        RemoteSandboxProvider remote = Connected(machine);

        // Recorded before the call, so a call by key finds the sandbox even if this one is interrupted.
        if ((await PlaceAsync(db, spec.Key.Value, machine, ct)).IsError(out Error? taken))
        {
            return new Result<SandboxObservation>(taken);
        }

        return await remote.CreateAsync(spec with { Location = null }, ct);
    }

    public async Task<Result<SandboxObservation>> SuspendAsync(SandboxKey key, CancellationToken ct)
    {
        RemoteSandboxProvider? remote = await RemoteOfAsync(key.Value, ct);
        return remote is null ? new Result<SandboxObservation>(SandboxNotFound) : await remote.SuspendAsync(key, ct);
    }

    public async Task<Result<SandboxObservation>> ResumeAsync(SandboxKey key, CancellationToken ct)
    {
        RemoteSandboxProvider? remote = await RemoteOfAsync(key.Value, ct);
        return remote is null ? new Result<SandboxObservation>(SandboxNotFound) : await remote.ResumeAsync(key, ct);
    }

    public async Task<SandboxObservation?> ObserveAsync(SandboxKey key, CancellationToken ct)
    {
        RemoteSandboxProvider? remote = await RemoteOfAsync(key.Value, ct);
        return remote is null ? null : await remote.ObserveAsync(key, ct);
    }

    // Only connected machines answer; a disconnected machine's sandboxes are missing from the list.
    public async IAsyncEnumerable<SandboxObservation> ListAsync([EnumeratorCancellation] CancellationToken ct)
    {
        foreach (RemoteSandboxProvider remote in connections.All())
        {
            await foreach (SandboxObservation sandbox in remote.ListAsync(ct))
            {
                yield return sandbox;
            }
        }
    }

    public async Task DeleteAsync(SandboxKey key, CancellationToken ct)
    {
        RemoteSandboxProvider? remote = await RemoteOfAsync(key.Value, ct);
        if (remote is not null)
        {
            await remote.DeleteAsync(key, ct);
            await UnplaceAsync(key.Value, ct);
        }
    }

    public async Task<Result<SnapshotObservation>> SnapshotAsync(SandboxKey sandbox, SnapshotKey snapshot, CancellationToken ct)
    {
        await using MachinesDbContext db = await databases.CreateDbContextAsync(ct);
        if (await PlacementAsync(db, sandbox.Value, ct) is not MachineId machine)
        {
            return new Result<SnapshotObservation>(SandboxNotFound);
        }

        RemoteSandboxProvider remote = Connected(machine);
        if ((await PlaceAsync(db, snapshot.Value, machine, ct)).IsError(out Error? taken))
        {
            return new Result<SnapshotObservation>(taken);
        }

        return await remote.SnapshotAsync(sandbox, snapshot, ct);
    }

    public async IAsyncEnumerable<SnapshotObservation> ListSnapshotsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        foreach (RemoteSandboxProvider remote in connections.All())
        {
            await foreach (SnapshotObservation snapshot in remote.ListSnapshotsAsync(ct))
            {
                yield return snapshot;
            }
        }
    }

    public async Task DeleteSnapshotAsync(SnapshotKey snapshot, CancellationToken ct)
    {
        RemoteSandboxProvider? remote = await RemoteOfAsync(snapshot.Value, ct);
        if (remote is not null)
        {
            await remote.DeleteSnapshotAsync(snapshot, ct);
            await UnplaceAsync(snapshot.Value, ct);
        }
    }

    private static async Task<MachineId?> PlacementAsync(MachinesDbContext db, Guid key, CancellationToken ct)
    {
        return await db.Placements.Where(placement => placement.Key == key).Select(placement => (MachineId?)placement.MachineId).SingleOrDefaultAsync(ct);
    }

    // Placing the same key on the same machine again is fine: provider calls are safe to repeat.
    private static async Task<Result> PlaceAsync(MachinesDbContext db, Guid key, MachineId machine, CancellationToken ct)
    {
        MachineId? placed = await PlacementAsync(db, key, ct);
        if (placed == machine)
        {
            return new Result(new Success());
        }

        if (placed is not null)
        {
            return new Result(Error.Conflict("sandboxing.key_in_use", "The key is in use on another machine."));
        }

        db.Placements.Add(new Placement(key, machine));
        return await db.SaveAsync(ct);
    }

    private RemoteSandboxProvider Connected(MachineId machine)
    {
        return connections.Find(machine)
            ?? throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"Machine {machine.Value} isn't connected."));
    }

    // The connection of the machine the key lives on; null when the key lives nowhere.
    private async Task<RemoteSandboxProvider?> RemoteOfAsync(Guid key, CancellationToken ct)
    {
        await using MachinesDbContext db = await databases.CreateDbContextAsync(ct);
        return await PlacementAsync(db, key, ct) is MachineId machine ? Connected(machine) : null;
    }

    private async Task UnplaceAsync(Guid key, CancellationToken ct)
    {
        await using MachinesDbContext db = await databases.CreateDbContextAsync(ct);

        // Placements have no rules to protect; the row just goes.
        await db.Placements.Where(placement => placement.Key == key).ExecuteDeleteAsync(ct);
    }
}
