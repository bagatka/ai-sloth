using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.Foundation;
using Wire = Bagatka.Sandboxing.Remote.V1;

namespace Bagatka.Sandboxing.Remote;

/// <summary>
/// An <see cref="ISandboxProvider"/> whose calls run on another provider, wherever its owner can reach
/// one. Each call goes out on <see cref="Calls"/>; the owner moves it, runs it with
/// <see cref="SandboxCalls.ExecuteAsync"/>, and hands back the result with <see cref="Complete"/>.
/// Results may arrive in any order.
/// </summary>
/// <remarks>
/// One instance serves one connection. <see cref="Disconnect"/> fails the calls in flight, and calls
/// made afterwards fail at once with <see cref="InvalidOperationException"/>, as an unreachable
/// backend would. A cancelled call stops waiting, but the remote side may still finish it; provider
/// operations are safe to repeat. <see cref="KeepAliveAsync"/> disconnects when the other side falls
/// silent. Thread-safe.
/// </remarks>
public sealed class RemoteSandboxProvider(string name) : ISandboxProvider
{
    // The owner sends calls as they come; the bound only stops a stuck connection from growing memory.
    private readonly Channel<Wire.SandboxCall> _calls = Channel.CreateBounded<Wire.SandboxCall>(
        new BoundedChannelOptions(256) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });

    private readonly Lock _gate = new Lock();
    private readonly Dictionary<string, TaskCompletionSource<Wire.SandboxCallResult>> _inFlight = new Dictionary<string, TaskCompletionSource<Wire.SandboxCallResult>>(StringComparer.Ordinal);
    private bool _disconnected;

    /// <inheritdoc />
    public string Name { get; } = name;

    /// <summary>The calls to run on the remote provider, in the order they were made.</summary>
    public ChannelReader<Wire.SandboxCall> Calls => _calls.Reader;

    /// <summary>Settles the call the result answers. A result for a call nobody waits for any more is ignored.</summary>
    public void Complete(Wire.SandboxCallResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        TaskCompletionSource<Wire.SandboxCallResult>? waiting;
        lock (_gate)
        {
            _inFlight.Remove(result.CallId, out waiting);
        }

        waiting?.TrySetResult(result);
    }

    /// <summary>The connection ended: fails the calls in flight and every call made from now on.</summary>
    public void Disconnect()
    {
        List<TaskCompletionSource<Wire.SandboxCallResult>> failed;
        lock (_gate)
        {
            _disconnected = true;
            failed = [.. _inFlight.Values];
            _inFlight.Clear();
        }

        _calls.Writer.TryComplete();
        foreach (TaskCompletionSource<Wire.SandboxCallResult> call in failed)
        {
            call.TrySetException(Disconnected());
        }
    }

    /// <summary>
    /// Asks the other side every <paramref name="every"/> whether it is there, and disconnects when it
    /// doesn't answer within <paramref name="within"/>, as when the computer at the other end went to
    /// sleep or off the network without closing the connection. Returns true then, and false when the
    /// connection ended otherwise or <paramref name="ct"/> was cancelled. The pings also keep proxies,
    /// which end streams idle for a few minutes, from cutting a connection that has nothing to do.
    /// </summary>
    public async Task<bool> KeepAliveAsync(TimeSpan every, TimeSpan within, TimeProvider time, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(time);
        using PeriodicTimer timer = new PeriodicTimer(every, time);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                using CancellationTokenSource late = new CancellationTokenSource(within, time);
                using CancellationTokenSource waiting = CancellationTokenSource.CreateLinkedTokenSource(ct, late.Token);
                try
                {
                    // Any answer will do: a side that doesn't know pings answers with a failure.
                    await CallAsync(new Wire.SandboxCall { Ping = new Wire.Ping() }, waiting.Token);
                }
                catch (OperationCanceledException) when (late.IsCancellationRequested && !ct.IsCancellationRequested)
                {
                    Disconnect();
                    return true;
                }
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The owner stopped keeping the connection alive.
        }
        catch (InvalidOperationException)
        {
            // The connection ended.
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<Result<SandboxObservation>> CreateAsync(SandboxSpec spec, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(spec);
        Wire.SandboxCallResult result = await CallAsync(new Wire.SandboxCall { Create = SandboxWire.ToWire(spec) }, ct);
        return Observation(result);
    }

    /// <inheritdoc />
    public async Task<Result<SandboxObservation>> SuspendAsync(SandboxKey key, CancellationToken ct)
    {
        Wire.SandboxCallResult result = await CallAsync(new Wire.SandboxCall { Suspend = SandboxWire.Key(key.Value) }, ct);
        return Observation(result);
    }

    /// <inheritdoc />
    public async Task<Result<SandboxObservation>> ResumeAsync(SandboxKey key, CancellationToken ct)
    {
        Wire.SandboxCallResult result = await CallAsync(new Wire.SandboxCall { Resume = SandboxWire.Key(key.Value) }, ct);
        return Observation(result);
    }

    /// <inheritdoc />
    public async Task<SandboxObservation?> ObserveAsync(SandboxKey key, CancellationToken ct)
    {
        Wire.SandboxCallResult result = await CallAsync(new Wire.SandboxCall { Observe = SandboxWire.Key(key.Value) }, ct);
        return result.ResultCase switch
        {
            Wire.SandboxCallResult.ResultOneofCase.Sandbox => SandboxWire.FromWire(result.Sandbox),
            Wire.SandboxCallResult.ResultOneofCase.Absent => null,
            _ => throw Unexpected(result),
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SandboxObservation> ListAsync([EnumeratorCancellation] CancellationToken ct)
    {
        Wire.SandboxCallResult result = await CallAsync(new Wire.SandboxCall { List = new Wire.ListSandboxes() }, ct);
        if (result.ResultCase != Wire.SandboxCallResult.ResultOneofCase.Sandboxes)
        {
            throw Unexpected(result);
        }

        foreach (Wire.Sandbox sandbox in result.Sandboxes.Items)
        {
            yield return SandboxWire.FromWire(sandbox);
        }
    }

    /// <inheritdoc />
    public async Task DeleteAsync(SandboxKey key, CancellationToken ct)
    {
        Wire.SandboxCallResult result = await CallAsync(new Wire.SandboxCall { Delete = SandboxWire.Key(key.Value) }, ct);
        Done(result);
    }

    /// <inheritdoc />
    public async Task<Result<SnapshotObservation>> SnapshotAsync(SandboxKey sandbox, SnapshotKey snapshot, CancellationToken ct)
    {
        Wire.CreateSnapshot call = new Wire.CreateSnapshot { Sandbox = SandboxWire.Key(sandbox.Value), Snapshot = SandboxWire.Key(snapshot.Value) };
        Wire.SandboxCallResult result = await CallAsync(new Wire.SandboxCall { Snapshot = call }, ct);
        return result.ResultCase switch
        {
            Wire.SandboxCallResult.ResultOneofCase.Snapshot => new Result<SnapshotObservation>(SandboxWire.FromWire(result.Snapshot)),
            Wire.SandboxCallResult.ResultOneofCase.Error => new Result<SnapshotObservation>(SandboxWire.FromWire(result.Error)),
            _ => throw Unexpected(result),
        };
    }

    /// <inheritdoc />
    public async IAsyncEnumerable<SnapshotObservation> ListSnapshotsAsync([EnumeratorCancellation] CancellationToken ct)
    {
        Wire.SandboxCallResult result = await CallAsync(new Wire.SandboxCall { ListSnapshots = new Wire.ListSnapshots() }, ct);
        if (result.ResultCase != Wire.SandboxCallResult.ResultOneofCase.Snapshots)
        {
            throw Unexpected(result);
        }

        foreach (Wire.Snapshot snapshot in result.Snapshots.Items)
        {
            yield return SandboxWire.FromWire(snapshot);
        }
    }

    /// <inheritdoc />
    public async Task DeleteSnapshotAsync(SnapshotKey snapshot, CancellationToken ct)
    {
        Wire.SandboxCallResult result = await CallAsync(new Wire.SandboxCall { DeleteSnapshot = SandboxWire.Key(snapshot.Value) }, ct);
        Done(result);
    }

    private static Result<SandboxObservation> Observation(Wire.SandboxCallResult result)
    {
        return result.ResultCase switch
        {
            Wire.SandboxCallResult.ResultOneofCase.Sandbox => new Result<SandboxObservation>(SandboxWire.FromWire(result.Sandbox)),
            Wire.SandboxCallResult.ResultOneofCase.Error => new Result<SandboxObservation>(SandboxWire.FromWire(result.Error)),
            _ => throw Unexpected(result),
        };
    }

    private static void Done(Wire.SandboxCallResult result)
    {
        if (result.ResultCase != Wire.SandboxCallResult.ResultOneofCase.Done)
        {
            throw Unexpected(result);
        }
    }

    // A failure on the remote side surfaces here as it would from a local provider: as an exception.
    private static InvalidOperationException Unexpected(Wire.SandboxCallResult result)
    {
        return result.ResultCase == Wire.SandboxCallResult.ResultOneofCase.Failure
            ? new InvalidOperationException("The remote provider failed: " + result.Failure.Message)
            : new InvalidOperationException("The remote provider answered with " + result.ResultCase + ".");
    }

    private static InvalidOperationException Disconnected()
    {
        return new InvalidOperationException("The remote provider is disconnected.");
    }

    private async Task<Wire.SandboxCallResult> CallAsync(Wire.SandboxCall call, CancellationToken ct)
    {
        call.CallId = Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture);
        TaskCompletionSource<Wire.SandboxCallResult> result = new TaskCompletionSource<Wire.SandboxCallResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_gate)
        {
            if (_disconnected)
            {
                throw Disconnected();
            }

            _inFlight.Add(call.CallId, result);
        }

        try
        {
            await _calls.Writer.WriteAsync(call, ct);
            return await result.Task.WaitAsync(ct);
        }
        catch (ChannelClosedException)
        {
            throw Disconnected();
        }
        finally
        {
            lock (_gate)
            {
                _inFlight.Remove(call.CallId);
            }
        }
    }
}
