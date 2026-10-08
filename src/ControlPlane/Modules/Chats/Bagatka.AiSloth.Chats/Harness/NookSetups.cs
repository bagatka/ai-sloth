using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;

namespace Bagatka.AiSloth.Chats.Harness;

// A nook's setup as chats work with it (Nooks runs it): following a run to its end, keeping the end of
// its output to show. What people see in a chat never names where files are in a nook.
internal sealed class NookSetups(INooksApi nooks)
{
    // How much of a failed setup's output a chat shows: its end, where the failure is.
    public const int MaxOutput = 2000;

    // Follows a setup run to its end, keeping the end of its output. A watch that fails ends as a
    // failed run, saying why.
    public async Task<SetupEnd> FollowAsync(NookId nookId, ProcessId run, CancellationToken ct)
    {
        try
        {
            return await ReadAsync(nookId, run, ct);
        }
        catch (Exception exception) when (!ct.IsCancellationRequested)
        {
            return new SetupEnd(ProcessExited.Lost, "Following the setup failed: " + exception.Message);
        }
    }

    private async Task<SetupEnd> ReadAsync(NookId nookId, ProcessId run, CancellationToken ct)
    {
        Result<IAsyncEnumerable<ProcessEvent>> watch = await nooks.WatchProcessAsync(SystemActors.Harness, new WatchProcess(nookId, run, FromOffset: 0), ct);
        if (watch.Failed)
        {
            return new SetupEnd(ProcessExited.Lost, "Following the setup failed: " + watch.Error.Message);
        }

        List<byte> kept = [];
        await foreach (ProcessEvent processEvent in watch.Output.WithCancellation(ct))
        {
            switch (processEvent.Value)
            {
                case ProcessOutput printed:
                    kept.AddRange(printed.Data.Span);
                    kept.RemoveRange(0, Math.Max(0, kept.Count - (MaxOutput * 4)));
                    break;
                case ProcessExited exited:
                    return new SetupEnd(exited.ExitCode, Tail(Encoding.UTF8.GetString([.. kept])));
            }
        }

        return new SetupEnd(ProcessExited.Lost, "The setup's output ended before it did.");
    }

    // The end of a setup's output, from the start of a line.
    private static string Tail(string output)
    {
        string trimmed = output.TrimEnd();
        if (trimmed.Length <= MaxOutput)
        {
            return trimmed;
        }

        string end = trimmed[^MaxOutput..];
        int line = end.IndexOf('\n', StringComparison.Ordinal);
        return line < 0 ? end : end[(line + 1)..];
    }
}
