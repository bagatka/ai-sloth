using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipelines;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;

namespace Bagatka.AiSloth.Nooks.Daemons;

// Bulk input waiting for one process: what a writer produces, read once by the process's daemon on a
// stream of its own. The writer runs while the daemon reads, through a small pipe that slows it to the
// daemon's pace, so the input is never held whole in memory.
internal sealed class InputFeed(NookId nookId, ProcessId processId, Func<Stream, CancellationToken, Task> write)
{
    // What the protocol allows per chunk (daemon.proto, ReadInput).
    private const int ChunkBytes = 64 * 1024;

    private readonly TaskCompletionSource _finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _taken;

    public NookId NookId { get; } = nookId;

    public ProcessId ProcessId { get; } = processId;

    // Completes when the writer finished, failing as it did, or when the feed ended unread.
    public Task Finished => _finished.Task;

    // True the first time only: a feed is read once.
    public bool Take()
    {
        return Interlocked.Exchange(ref _taken, 1) == 0;
    }

    // The process ended without its daemon reading the feed.
    public void Abandon()
    {
        if (Take())
        {
            _finished.TrySetResult();
        }
    }

    // The writer's bytes in chunks; a writer that fails ends them with an IOException, so the daemon
    // cuts the process's input short.
    public async IAsyncEnumerable<ReadOnlyMemory<byte>> ReadAsync([EnumeratorCancellation] CancellationToken ct)
    {
        Pipe pipe = new Pipe(new PipeOptions(pauseWriterThreshold: 1024 * 1024, resumeWriterThreshold: 512 * 1024));
        using CancellationTokenSource reading = CancellationTokenSource.CreateLinkedTokenSource(ct);
        Task writing = WriteAsync(pipe.Writer, reading.Token);
        try
        {
            while (true)
            {
                ReadResult read = await pipe.Reader.ReadAsync(ct);
                foreach (ReadOnlyMemory<byte> segment in read.Buffer)
                {
                    for (int start = 0; start < segment.Length; start += ChunkBytes)
                    {
                        yield return segment.Slice(start, Math.Min(ChunkBytes, segment.Length - start)).ToArray();
                    }
                }

                pipe.Reader.AdvanceTo(read.Buffer.End);
                if (read.IsCompleted)
                {
                    break;
                }
            }
        }
        finally
        {
            await reading.CancelAsync();
            await pipe.Reader.CompleteAsync();
            await Task.WhenAny(writing);
            _finished.TrySetFromTask(writing);
        }
    }

    private async Task WriteAsync(PipeWriter writer, CancellationToken ct)
    {
        bool written = false;
        try
        {
            await using Stream stream = writer.AsStream(leaveOpen: true);
            await write(stream, ct);
            written = true;
        }
        finally
        {
            await writer.CompleteAsync(written ? null : new IOException("The input broke off before it ended."));
        }
    }
}
