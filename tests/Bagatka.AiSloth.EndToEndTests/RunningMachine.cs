using System;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Cli;
using Bagatka.Sandboxing;
using Bagatka.Sandboxing.Docker;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Machine mode running in the test process against the local Docker Engine. Disposing it stops it;
/// the sandboxes stay, as they do when <c>sloth machine run</c> stops.
/// </summary>
internal sealed class RunningMachine : IAsyncDisposable
{
    private readonly ServiceProvider _services;
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();

    public RunningMachine(MachineCredential credential, DockerSandboxSettings docker)
    {
        _services = new ServiceCollection().AddDockerSandboxProvider(docker).BuildServiceProvider();
        MachineLink link = new MachineLink(credential, _services.GetRequiredService<ISandboxProvider>(), TimeProvider.System, NullLogger<MachineLink>.Instance);
        Running = link.RunAsync(_stopping.Token);
    }

    /// <summary>Completes when machine mode stops on its own, which means the machine was removed.</summary>
    public Task Running { get; }

    public async ValueTask DisposeAsync()
    {
        await _stopping.CancelAsync();
        try
        {
            await Running;
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
            // Stopped.
        }

        await _services.DisposeAsync();
        _stopping.Dispose();
    }
}
