using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.DaemonProtocol.V1;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// One process's output on disk. Standard output and standard error share one sequence of byte
/// offsets, so a watcher sees them in the order they were written. Output lives in segment files of
/// frames (channel, length, bytes); retention drops whole segments, oldest first.
/// </summary>
/// <remarks>
/// Two writers (the output pumps) and any number of readers may use a journal at once. With
/// <see cref="OutputRetention.Complete"/>, writers wait in <see cref="WaitForRoomAsync"/> while too
/// much output is undelivered, so the process blocks instead of output being dropped.
/// </remarks>
internal sealed class OutputJournal : IDisposable
{
    private const int FrameHeaderBytes = 5;

    private readonly Lock _gate = new Lock();
    private readonly string _directory;
    private readonly OutputRetention _retention;
    private readonly OutputLimits _limits;
    private readonly List<Segment> _segments = [];
    private FileStream? _writer;
    private long _length;
    private long _delivered;
    private bool _complete;
    private TaskCompletionSource _changed = NewSignal();

    public OutputJournal(string directory, OutputRetention retention, OutputLimits limits)
    {
        if (retention is not (OutputRetention.Recent or OutputRetention.Complete))
        {
            throw new ArgumentOutOfRangeException(nameof(retention), retention, "Output retention must be Recent or Complete.");
        }

        _directory = directory;
        _retention = retention;
        _limits = limits;
        Directory.CreateDirectory(directory);
    }

    /// <summary>How many bytes of output the process has written.</summary>
    public long Length
    {
        get
        {
            lock (_gate)
            {
                return _length;
            }
        }
    }

    /// <summary>
    /// Returns once a writer may append: at once for <see cref="OutputRetention.Recent"/>, and for
    /// <see cref="OutputRetention.Complete"/> once undelivered output is below the limit.
    /// </summary>
    public async Task WaitForRoomAsync(CancellationToken ct)
    {
        while (true)
        {
            Task changed;
            lock (_gate)
            {
                if (_retention == OutputRetention.Recent || _length - _delivered < _limits.UndeliveredBytes)
                {
                    return;
                }

                changed = _changed.Task;
            }

            await changed.WaitAsync(ct);
        }
    }

    /// <summary>
    /// Appends a chunk the process wrote to one channel. Returns false, having appended nothing, when
    /// the disk has no room for it.
    /// </summary>
    public bool TryAppend(OutputChannel channel, ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
        {
            return true;
        }

        lock (_gate)
        {
            if (_complete)
            {
                throw new InvalidOperationException("The journal is complete.");
            }

            FileStream writer;
            try
            {
                writer = _writer is not null && _segments[^1].Length < _limits.SegmentBytes ? _writer : StartSegment();
            }
            catch (IOException)
            {
                return false;
            }

            long frameStart = writer.Position;
            Span<byte> header = stackalloc byte[FrameHeaderBytes];
            header[0] = (byte)channel;
            BinaryPrimitives.WriteInt32LittleEndian(header[1..], data.Length);
            try
            {
                writer.Write(header);
                writer.Write(data);
            }
            catch (IOException)
            {
                // Nothing counts until the whole frame is written, so cut off whatever part of it was.
                writer.SetLength(frameStart);
                writer.Position = frameStart;
                return false;
            }

            _segments[^1] = _segments[^1] with { Length = _segments[^1].Length + data.Length };
            _length += data.Length;
            Trim();
            Signal();
            return true;
        }
    }

    /// <summary>Records that output up to <paramref name="offset"/> has been delivered to a watcher.</summary>
    public void MarkDelivered(long offset)
    {
        lock (_gate)
        {
            if (offset <= _delivered)
            {
                return;
            }

            _delivered = offset;
            Trim();
            Signal();
        }
    }

    /// <summary>Records that the process has written its last output.</summary>
    public void Complete()
    {
        lock (_gate)
        {
            _complete = true;
            _writer?.Dispose();
            _writer = null;
            Signal();
        }
    }

    /// <summary>
    /// The output from an offset, first what is kept and then live, until the process's last output.
    /// An offset that is no longer kept starts at the earliest byte kept.
    /// </summary>
    public async IAsyncEnumerable<OutputChunk> ReadAsync(long fromOffset, [EnumeratorCancellation] CancellationToken ct)
    {
        await using SegmentCursor cursor = new SegmentCursor();
        long next = Math.Max(fromOffset, 0);
        while (true)
        {
            ReadPosition position = Locate(next);
            next = position.Next;
            if (position.Segment is null)
            {
                if (position.Complete)
                {
                    yield break;
                }

                await position.Changed.WaitAsync(ct);
                continue;
            }

            bool opened = cursor.TryOpen(position.Segment);
            if (!opened)
            {
                // Retention dropped the segment meanwhile; the next pass starts at the earliest byte kept.
                continue;
            }

            await foreach (OutputChunk chunk in cursor.ReadAsync(position.Segment, next, ct))
            {
                yield return chunk;
            }

            next = Math.Max(next, cursor.Offset);
        }
    }

    /// <summary>Deletes the journal and its files.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _complete = true;
            _writer?.Dispose();
            _writer = null;
            _segments.Clear();
            Signal();
        }

        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static TaskCompletionSource NewSignal()
    {
        return new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    // Where a reader stands: the segment holding the next byte, or none when the reader has caught up
    // (Complete says whether more output can still come).
    private ReadPosition Locate(long next)
    {
        lock (_gate)
        {
            if (_segments.Count > 0 && next < _segments[0].Start)
            {
                next = _segments[0].Start;
            }

            Segment? segment = _segments.Find(candidate => candidate.Start <= next && next < candidate.Start + candidate.Length);
            return new ReadPosition(next, segment, segment is null && _complete, _changed.Task);
        }
    }

    // Callers hold _gate. Unbuffered, so a failed write leaves nothing behind to be flushed later.
    private FileStream StartSegment()
    {
        string path = Path.Combine(_directory, _length.ToString("D20", CultureInfo.InvariantCulture));
        FileStream writer = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, bufferSize: 0);
        _writer?.Dispose();
        _writer = writer;
        _segments.Add(new Segment(_length, 0, path));
        return writer;
    }

    // Callers hold _gate. Drops whole segments that retention no longer needs; the newest always stays.
    private void Trim()
    {
        long keepFrom = _retention == OutputRetention.Recent
            ? _length - _limits.RecentBytes
            : _delivered - _limits.RecentBytes;
        while (_segments.Count > 1 && _segments[0].Start + _segments[0].Length <= keepFrom)
        {
            File.Delete(_segments[0].Path);
            _segments.RemoveAt(0);
        }
    }

    // Callers hold _gate. Wakes every waiting reader and writer.
    private void Signal()
    {
        TaskCompletionSource changed = _changed;
        _changed = NewSignal();
        changed.TrySetResult();
    }

    private sealed record Segment(long Start, long Length, string Path);

    private readonly record struct ReadPosition(long Next, Segment? Segment, bool Complete, Task Changed);

    // A reader's open segment file and how far into the output it has read.
    private sealed class SegmentCursor : IAsyncDisposable
    {
        private readonly byte[] _header = new byte[FrameHeaderBytes];
        private FileStream? _file;
        private string? _path;

        // The output offset of the next frame in the open segment.
        public long Offset { get; private set; }

        // Opens the segment unless it is already open; false when retention deleted it meanwhile.
        public bool TryOpen(Segment segment)
        {
            if (string.Equals(_path, segment.Path, StringComparison.Ordinal))
            {
                return true;
            }

            _file?.Dispose();
            _file = null;
            _path = null;
            try
            {
                _file = new FileStream(segment.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            }
            catch (FileNotFoundException)
            {
                return false;
            }

            _path = segment.Path;
            Offset = segment.Start;
            return true;
        }

        // Reads the segment's frames up to its recorded length, which only covers fully written
        // frames, skipping output before `from`.
        public async IAsyncEnumerable<OutputChunk> ReadAsync(Segment segment, long from, [EnumeratorCancellation] CancellationToken ct)
        {
            if (_file is null)
            {
                throw new InvalidOperationException("No segment is open.");
            }

            FileStream file = _file;
            long end = segment.Start + segment.Length;
            while (Offset < end)
            {
                await file.ReadExactlyAsync(_header, ct);
                OutputChannel channel = (OutputChannel)_header[0];
                int length = BinaryPrimitives.ReadInt32LittleEndian(_header.AsSpan(1));
                long skip = Math.Max(0, from - Offset);
                if (skip >= length)
                {
                    file.Seek(length, SeekOrigin.Current);
                }
                else
                {
                    byte[] data = new byte[length];
                    await file.ReadExactlyAsync(data, ct);
                    yield return new OutputChunk(Offset + skip, channel, data.AsMemory((int)skip));
                }

                Offset += length;
            }
        }

        public ValueTask DisposeAsync()
        {
            return _file?.DisposeAsync() ?? ValueTask.CompletedTask;
        }
    }
}
