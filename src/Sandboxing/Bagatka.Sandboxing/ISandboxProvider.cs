using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;

namespace Bagatka.Sandboxing;

/// <summary>
/// Runs sandboxes on one compute backend, such as Docker or Azure Container Apps Sandboxes. It
/// manages lifecycle only: everything that happens inside a sandbox goes through the image's entry
/// point, never through the provider.
/// </summary>
/// <remarks>
/// <para>
/// A sandbox's files survive every operation except <see cref="DeleteAsync"/>, on every provider.
/// Memory survives suspension only where the backend can keep it, and the provider reports which
/// happened: <see cref="SandboxState.Paused"/> (memory and files kept, processes continue) or
/// <see cref="SandboxState.Stopped"/> (files kept, processes start again). Every provider implements
/// every member; there are no capability flags.
/// </para>
/// <para>
/// Every operation is safe to repeat. A provider tags each resource it creates with its deployment
/// scope (from its settings) and the caller's key, and lists or touches only resources in its own
/// scope, so several deployments can share one cloud account.
/// </para>
/// <para>
/// Expected outcomes are returned as errors. Infrastructure failures, such as an unreachable
/// backend, throw. Implementations are thread-safe.
/// </para>
/// </remarks>
public interface ISandboxProvider
{
    /// <summary>
    /// A stable, lowercase name such as <c>docker</c>. Callers persist it, so it never changes.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Creates the sandbox and starts its entry point, returning once the backend has accepted it,
    /// usually in <see cref="SandboxState.Starting"/>. A sandbox created from a snapshot starts with
    /// the snapshot's files, a fresh entry point, and the spec's environment; memory is never
    /// restored from a snapshot. Repeating the call with the same spec returns the existing sandbox.
    /// </summary>
    /// <returns>
    /// The sandbox; a validation error when the backend can't run the spec; not found when the source
    /// snapshot doesn't exist; or a conflict when the key exists with a different spec.
    /// </returns>
    public Task<Result<SandboxObservation>> CreateAsync(SandboxSpec spec, CancellationToken ct);

    /// <summary>
    /// Releases the sandbox's compute, keeping its memory when the backend can and its files always.
    /// Suspending a suspended sandbox returns it as it is.
    /// </summary>
    /// <returns>The sandbox, <see cref="SandboxState.Paused"/> or <see cref="SandboxState.Stopped"/>; or not found.</returns>
    public Task<Result<SandboxObservation>> SuspendAsync(SandboxKey key, CancellationToken ct);

    /// <summary>
    /// Gives a suspended sandbox compute again, returning once the backend has accepted it. Resuming
    /// a running sandbox returns it as it is.
    /// </summary>
    /// <returns>The sandbox; or not found.</returns>
    public Task<Result<SandboxObservation>> ResumeAsync(SandboxKey key, CancellationToken ct);

    /// <summary>
    /// The sandbox as the backend sees it now, or <see langword="null"/> when it doesn't exist.
    /// </summary>
    public Task<SandboxObservation?> ObserveAsync(SandboxKey key, CancellationToken ct);

    /// <summary>
    /// Every sandbox in this provider's scope, in no particular order, streamed so a large deployment
    /// is never held in memory. Used to reconcile recorded and actual state.
    /// </summary>
    public IAsyncEnumerable<SandboxObservation> ListAsync(CancellationToken ct);

    /// <summary>
    /// Deletes the sandbox, its files, and everything the provider created for it. Deleting a sandbox
    /// that doesn't exist succeeds. Snapshots taken from it are unaffected.
    /// </summary>
    public Task DeleteAsync(SandboxKey key, CancellationToken ct);

    /// <summary>
    /// Captures the sandbox's files as a snapshot that new sandboxes can be created from, returning
    /// once the snapshot is usable, which can take a while for large disks. The sandbox keeps its
    /// state, though it may pause briefly. Repeating the call with the same keys returns the existing
    /// snapshot.
    /// </summary>
    /// <returns>The snapshot; not found when the sandbox doesn't exist; or a conflict when the snapshot key was used for another sandbox.</returns>
    public Task<Result<SnapshotObservation>> SnapshotAsync(SandboxKey sandbox, SnapshotKey snapshot, CancellationToken ct);

    /// <summary>
    /// Every usable snapshot in this provider's scope, in no particular order. Used to reconcile.
    /// </summary>
    public IAsyncEnumerable<SnapshotObservation> ListSnapshotsAsync(CancellationToken ct);

    /// <summary>
    /// Deletes a snapshot. Deleting a snapshot that doesn't exist succeeds, and sandboxes created from
    /// it are never affected.
    /// </summary>
    public Task DeleteSnapshotAsync(SnapshotKey snapshot, CancellationToken ct);
}
