using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.Sandboxing.Remote;
using Bagatka.Sandboxing.Remote.V1;

namespace Bagatka.Sandboxing.ConformanceTests;

/// <summary>
/// Runs a <see cref="RemoteSandboxProvider"/>'s calls on a local provider, as a machine's connection
/// does, with nothing in between, so the suite proves every operation survives the trip.
/// </summary>
internal sealed class RemoteLoop : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = new CancellationTokenSource();
    private readonly Task _running;

    public RemoteLoop(ISandboxProvider local)
    {
        Provider = new RemoteSandboxProvider("remote");
        _running = RunAsync(local);
    }

    public RemoteSandboxProvider Provider { get; }

    public async ValueTask DisposeAsync()
    {
        Provider.Disconnect();
        await _stop.CancelAsync();
        await _running;
        _stop.Dispose();
    }

    // Calls run concurrently, as they do on a machine.
    private async Task RunAsync(ISandboxProvider local)
    {
        List<Task> running = [];
        await foreach (SandboxCall call in Provider.Calls.ReadAllAsync(CancellationToken.None))
        {
            running.Add(Task.Run(async () =>
            {
                SandboxCallResult result = await SandboxCalls.ExecuteAsync(local, call, _stop.Token);
                Provider.Complete(result);
            }));
        }

        try
        {
            await Task.WhenAll(running);
        }
        catch (OperationCanceledException)
        {
            // Disposed while calls ran.
        }
    }
}
