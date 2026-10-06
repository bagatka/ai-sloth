using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Wire = Bagatka.Sandboxing.Remote.V1;

namespace Bagatka.Sandboxing.Remote;

/// <summary>
/// The receiving side of <see cref="RemoteSandboxProvider"/>: runs calls on a local provider.
/// </summary>
public static class SandboxCalls
{
    /// <summary>
    /// Runs the call on <paramref name="provider"/> and describes the outcome. An exception becomes a
    /// failure result, so the caller learns what went wrong instead of waiting; only cancellation
    /// throws.
    /// </summary>
    public static async Task<Wire.SandboxCallResult> ExecuteAsync(ISandboxProvider provider, Wire.SandboxCall call, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(provider);
        ArgumentNullException.ThrowIfNull(call);
        Wire.SandboxCallResult result;
        try
        {
            result = await RunAsync(provider, call, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            result = new Wire.SandboxCallResult { Failure = new Wire.Failure { Message = exception.Message } };
        }

        result.CallId = call.CallId;
        return result;
    }

    private static async Task<Wire.SandboxCallResult> RunAsync(ISandboxProvider provider, Wire.SandboxCall call, CancellationToken ct)
    {
        switch (call.CallCase)
        {
            case Wire.SandboxCall.CallOneofCase.Create:
                Result<SandboxObservation> created = await provider.CreateAsync(SandboxWire.FromWire(call.Create), ct);
                return Observation(created);
            case Wire.SandboxCall.CallOneofCase.Suspend:
                Result<SandboxObservation> suspended = await provider.SuspendAsync(SandboxKey.From(SandboxWire.Key(call.Suspend)), ct);
                return Observation(suspended);
            case Wire.SandboxCall.CallOneofCase.Resume:
                Result<SandboxObservation> resumed = await provider.ResumeAsync(SandboxKey.From(SandboxWire.Key(call.Resume)), ct);
                return Observation(resumed);
            case Wire.SandboxCall.CallOneofCase.Observe:
                SandboxObservation? observed = await provider.ObserveAsync(SandboxKey.From(SandboxWire.Key(call.Observe)), ct);
                return observed is null
                    ? new Wire.SandboxCallResult { Absent = new Wire.Absent() }
                    : new Wire.SandboxCallResult { Sandbox = SandboxWire.ToWire(observed) };
            case Wire.SandboxCall.CallOneofCase.List:
                Wire.Sandboxes sandboxes = new Wire.Sandboxes();
                await foreach (SandboxObservation sandbox in provider.ListAsync(ct))
                {
                    sandboxes.Items.Add(SandboxWire.ToWire(sandbox));
                }

                return new Wire.SandboxCallResult { Sandboxes = sandboxes };
            case Wire.SandboxCall.CallOneofCase.Delete:
                await provider.DeleteAsync(SandboxKey.From(SandboxWire.Key(call.Delete)), ct);
                return new Wire.SandboxCallResult { Done = new Wire.Done() };
            case Wire.SandboxCall.CallOneofCase.Snapshot:
                Result<SnapshotObservation> snapshot = await provider.SnapshotAsync(
                    SandboxKey.From(SandboxWire.Key(call.Snapshot.Sandbox)),
                    SnapshotKey.From(SandboxWire.Key(call.Snapshot.Snapshot)),
                    ct);
                if (snapshot.Failed)
                {
                    return new Wire.SandboxCallResult { Error = SandboxWire.ToWire(snapshot.Error) };
                }

                return new Wire.SandboxCallResult { Snapshot = SandboxWire.ToWire(snapshot.Output) };
            case Wire.SandboxCall.CallOneofCase.ListSnapshots:
                Wire.Snapshots snapshots = new Wire.Snapshots();
                await foreach (SnapshotObservation listed in provider.ListSnapshotsAsync(ct))
                {
                    snapshots.Items.Add(SandboxWire.ToWire(listed));
                }

                return new Wire.SandboxCallResult { Snapshots = snapshots };
            case Wire.SandboxCall.CallOneofCase.DeleteSnapshot:
                await provider.DeleteSnapshotAsync(SnapshotKey.From(SandboxWire.Key(call.DeleteSnapshot)), ct);
                return new Wire.SandboxCallResult { Done = new Wire.Done() };
            case Wire.SandboxCall.CallOneofCase.Ping:
                return new Wire.SandboxCallResult { Done = new Wire.Done() };
            case Wire.SandboxCall.CallOneofCase.None:
                // A call from a newer caller than this provider knows.
                return new Wire.SandboxCallResult { Failure = new Wire.Failure { Message = "This provider doesn't know the call." } };
        }

        throw new InvalidOperationException("Unknown call " + call.CallCase + ".");
    }

    private static Wire.SandboxCallResult Observation(Result<SandboxObservation> result)
    {
        if (result.Failed)
        {
            return new Wire.SandboxCallResult { Error = SandboxWire.ToWire(result.Error) };
        }

        return new Wire.SandboxCallResult { Sandbox = SandboxWire.ToWire(result.Output) };
    }
}
