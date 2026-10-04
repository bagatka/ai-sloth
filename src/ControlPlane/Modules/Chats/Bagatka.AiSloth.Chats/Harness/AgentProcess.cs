using System;
using System.Buffers;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Bagatka.Harnesses;
using Microsoft.Extensions.DependencyInjection;

namespace Bagatka.AiSloth.Chats.Harness;

// An agent's process in its nook, seen as lines of text: starts it, reads its standard output a line at
// a time from any offset, writes lines to its standard input, and stops it. Knows nothing about ACP or
// chats; every call goes to Nooks as the harness system actor.
internal sealed class AgentProcess(IServiceScopeFactory scopes)
{
    // Where agents work: every nook's sources are mounted under it.
    public const string WorkingDirectory = "/work";

    // SendInput's limit, and the longest line an agent may write before something is clearly off.
    private const int MaxInputBytes = 64 * 1024;
    private const int MaxLineBytes = 32 * 1024 * 1024;

    // Keeps all the output, so reading can resume from any offset after a restart.
    public async Task<Result<ProcessId>> StartAsync(NookId nookId, HarnessProfile harness, IReadOnlyDictionary<string, string> environment, CancellationToken ct)
    {
        StartProcess start = new StartProcess(nookId, harness.Command, harness.Arguments, WorkingDirectory, OutputRetention.Complete, environment);
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Result<ProcessSummary> started = await scope.ServiceProvider.GetRequiredService<INooksApi>().StartProcessAsync(SystemActors.Harness, start, ct);
        if (started.Failed)
        {
            return new Result<ProcessId>(started.Error);
        }

        return new Result<ProcessId>(started.Output.Id);
    }

    // The complete lines of standard output after the offset, each with the offset just past it, then the
    // exit. Standard error is the agent's own log; it may hold anything, so it is skipped. A nook that is
    // gone took the process with it, which reads as an exit of -1; one that isn't ready may be back later,
    // so reading throws and the caller tries again.
    public async IAsyncEnumerable<AgentOutput> ReadAsync(NookId nookId, ProcessId processId, long offset, [EnumeratorCancellation] CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        Result<IAsyncEnumerable<ProcessEvent>> watched = await scope.ServiceProvider.GetRequiredService<INooksApi>()
            .WatchProcessAsync(SystemActors.Harness, new WatchProcess(nookId, processId, offset), ct);
        if (watched.Failed)
        {
            bool mayComeBack = watched.Error == NooksErrors.NotReady;
            if (mayComeBack)
            {
                throw new InvalidOperationException(watched.Error.Message);
            }

            yield return new AgentOutput(new HarnessExited(-1));
            yield break;
        }

        ArrayBufferWriter<byte> pending = new ArrayBufferWriter<byte>();
        await foreach (ProcessEvent processEvent in watched.Output.WithCancellation(ct))
        {
            switch (processEvent)
            {
                case ProcessOutput output when output.Channel == OutputChannel.StandardOutput:
                    ReadOnlyMemory<byte> data = output.Data;
                    int newline = data.Span.IndexOf((byte)'\n');
                    while (newline >= 0)
                    {
                        pending.Write(data.Span[..newline]);
                        long end = output.Offset + (output.Data.Length - data.Length) + newline + 1;
                        yield return new AgentOutput(new HarnessLine(Encoding.UTF8.GetString(pending.WrittenSpan), end));
                        pending.ResetWrittenCount();
                        data = data[(newline + 1)..];
                        newline = data.Span.IndexOf((byte)'\n');
                    }

                    pending.Write(data.Span);
                    if (pending.WrittenCount > MaxLineBytes)
                    {
                        throw new InvalidOperationException("Process " + processId.Value + " wrote a line longer than 32 MiB.");
                    }

                    break;
                case ProcessOutput:
                    break;
                case ProcessExited exited:
                    yield return new AgentOutput(new HarnessExited(exited.ExitCode));
                    yield break;
            }
        }
    }

    // Writes each line, in chunks SendInput accepts. Fails when the process is gone or out of reach; its
    // exit then arrives through ReadAsync.
    public async Task<Result> SendAsync(NookId nookId, ProcessId processId, IReadOnlyList<string> lines, CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        INooksApi nooks = scope.ServiceProvider.GetRequiredService<INooksApi>();
        foreach (string line in lines)
        {
            ReadOnlyMemory<byte> bytes = Encoding.UTF8.GetBytes(line + "\n");
            for (int start = 0; start < bytes.Length; start += MaxInputBytes)
            {
                ReadOnlyMemory<byte> chunk = bytes[start..Math.Min(bytes.Length, start + MaxInputBytes)];
                Result sent = await nooks.SendInputAsync(SystemActors.Harness, new SendInput(nookId, processId, chunk), ct);
                if (sent.Failed)
                {
                    return sent;
                }
            }
        }

        return new Result(new Success());
    }

    public async Task<Result> StopAsync(NookId nookId, ProcessId processId, CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INooksApi>().StopProcessAsync(SystemActors.Harness, new StopProcess(nookId, processId), ct);
    }
}
