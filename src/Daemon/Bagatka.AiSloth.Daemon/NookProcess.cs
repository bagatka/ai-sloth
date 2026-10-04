using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.DaemonProtocol.V1;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// One process the daemon started. It runs until it exits or is stopped, independent of the
/// control-plane connection; its output goes to its journal, and its exit is reported once.
/// </summary>
internal sealed class NookProcess : IAsyncDisposable
{
    // Like a shell, a program that can't start exits with 127.
    private const int CannotStart = 127;

    // After the process exits, its output pipes may stay open if it left children behind.
    private static readonly TimeSpan DrainTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan DiskFullRetryDelay = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan StopGracePeriod = TimeSpan.FromSeconds(10);

    // Null when the program couldn't start. Disposed only with this object, so killing it is safe
    // after it exits.
    private readonly Process? _process;
    private readonly int _chunkBytes;
    private readonly NookDisk _disk;
    private readonly Channel<ReadOnlyMemory<byte>> _input = Channel.CreateBounded<ReadOnlyMemory<byte>>(
        new BoundedChannelOptions(1024) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly CancellationTokenSource _pumps = new CancellationTokenSource();
    private Task? _stopping;

    private NookProcess(string id, OutputJournal output, Process? process, int chunkBytes, NookDisk disk, ChannelWriter<ProcessExited> exits)
    {
        Id = id;
        Output = output;
        _process = process;
        _chunkBytes = chunkBytes;
        _disk = disk;
        Exited = process is null ? Task.FromResult(CannotStart) : RunAsync(process, exits);
    }

    /// <summary>The control plane's ID for the process.</summary>
    public string Id { get; }

    /// <summary>Everything the process printed.</summary>
    public OutputJournal Output { get; }

    /// <summary>Completes with the exit code when the process has exited and its output is complete.</summary>
    public Task<int> Exited { get; }

    /// <summary>
    /// Starts a program directly, without a shell. A program that can't start becomes a process that
    /// exited with 127 after saying why on standard error. The daemon's own environment variables
    /// (<c>SLOTHD_*</c>) are not passed on; the instruction's are added.
    /// </summary>
    public static NookProcess Start(
        StartProcess instruction,
        string defaultWorkingDirectory,
        OutputJournal output,
        int chunkBytes,
        NookDisk disk,
        ChannelWriter<ProcessExited> exits)
    {
        ProcessStartInfo start = new ProcessStartInfo(instruction.Command)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = instruction.WorkingDirectory.Length > 0 ? instruction.WorkingDirectory : defaultWorkingDirectory,
        };

        foreach (string argument in instruction.Arguments)
        {
            start.ArgumentList.Add(argument);
        }

        foreach (string name in start.Environment.Keys.Where(name => name.StartsWith("SLOTHD_", StringComparison.Ordinal)).ToList())
        {
            start.Environment.Remove(name);
        }

        foreach (KeyValuePair<string, string> variable in instruction.Environment)
        {
            start.Environment[variable.Key] = variable.Value;
        }

        try
        {
            Process process = Process.Start(start)
                ?? throw new InvalidOperationException("The operating system started no process for " + instruction.Command + ".");
            return new NookProcess(instruction.ProcessId, output, process, chunkBytes, disk, exits);
        }
        catch (Win32Exception exception)
        {
            // On a full disk the explanation is lost; the exit code still says the program didn't start.
            _ = output.TryAppend(OutputChannel.StandardError, Encoding.UTF8.GetBytes("slothd: cannot start '" + instruction.Command + "': " + exception.Message + "\n"));
            output.Complete();
            exits.TryWrite(new ProcessExited { ProcessId = instruction.ProcessId, ExitCode = CannotStart });
            return new NookProcess(instruction.ProcessId, output, process: null, chunkBytes, disk, exits);
        }
    }

    /// <summary>Queues bytes for the process's standard input; ignored once the process has exited.</summary>
    public async Task SendInputAsync(ReadOnlyMemory<byte> data, CancellationToken ct)
    {
        try
        {
            await _input.Writer.WriteAsync(data, ct);
        }
        catch (ChannelClosedException)
        {
            // The process exited; there is nothing left to read the input.
        }
    }

    /// <summary>
    /// Starts stopping the process: SIGTERM now, a kill after the grace period. Returns at once; the
    /// exit is reported as usual.
    /// </summary>
    public void RequestStop()
    {
        if (_process is null || Exited.IsCompleted || _stopping is not null)
        {
            return;
        }

        _stopping = StopAsync(_process);
    }

    /// <summary>Kills the process if it still runs and deletes its output.</summary>
    public async ValueTask DisposeAsync()
    {
        if (_process is not null && !Exited.IsCompleted)
        {
            _process.Kill(entireProcessTree: true);
        }

        await _pumps.CancelAsync();
        await Task.WhenAll(Exited, _stopping ?? Task.CompletedTask);
        _process?.Dispose();
        _pumps.Dispose();
        Output.Dispose();
    }

    private async Task<int> RunAsync(Process process, ChannelWriter<ProcessExited> exits)
    {
        Task standardOutput = PumpAsync(process.StandardOutput.BaseStream, OutputChannel.StandardOutput);
        Task standardError = PumpAsync(process.StandardError.BaseStream, OutputChannel.StandardError);
        Task input = FeedInputAsync(process.StandardInput.BaseStream);

        // Deliberately not cancellable: the process outlives connections, and only a stop or the
        // daemon's own shutdown (which kills it) ends it.
        await process.WaitForExitAsync(CancellationToken.None);
        int exitCode = process.ExitCode;

        await Task.WhenAny(Task.WhenAll(standardOutput, standardError), Task.Delay(DrainTimeout, CancellationToken.None));
        await _pumps.CancelAsync();
        _input.Writer.TryComplete();
        await Task.WhenAll(standardOutput, standardError, input);

        Output.Complete();
        exits.TryWrite(new ProcessExited { ProcessId = Id, ExitCode = exitCode });
        return exitCode;
    }

    private async Task PumpAsync(Stream stream, OutputChannel channel)
    {
        byte[] buffer = new byte[_chunkBytes];
        try
        {
            while (true)
            {
                await Output.WaitForRoomAsync(_pumps.Token);
                int read = await stream.ReadAsync(buffer, _pumps.Token);
                if (read == 0)
                {
                    return;
                }

                // A full disk: keep the chunk and stop reading, so the process waits instead of losing output.
                while (!Output.TryAppend(channel, buffer.AsSpan(0, read)))
                {
                    _disk.Filled();
                    await Task.Delay(DiskFullRetryDelay, _pumps.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_pumps.IsCancellationRequested)
        {
            // The process exited and its pipes stayed open, or the daemon is shutting down.
        }
    }

    private async Task FeedInputAsync(Stream input)
    {
        try
        {
            await foreach (ReadOnlyMemory<byte> data in _input.Reader.ReadAllAsync(_pumps.Token))
            {
                await input.WriteAsync(data, _pumps.Token);
                await input.FlushAsync(_pumps.Token);
            }
        }
        catch (IOException)
        {
            // The process closed its standard input; later input has nowhere to go.
        }
        catch (OperationCanceledException) when (_pumps.IsCancellationRequested)
        {
            // The process exited.
        }
        finally
        {
            await input.DisposeAsync();
        }
    }

    private async Task StopAsync(Process process)
    {
        PosixSignals.Terminate(process.Id);
        if (await Task.WhenAny(Exited, Task.Delay(StopGracePeriod, CancellationToken.None)) != Exited)
        {
            process.Kill(entireProcessTree: true);
        }
    }
}
