using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.DaemonProtocol.V1;
using Google.Protobuf;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Daemon;

/// <summary>
/// The daemon's side of the protocol in <c>daemon.proto</c>: keeps one control stream open, carries
/// out instructions, and serves each watch on an upload stream of its own. Processes never depend on
/// the connection; losing it only means reconnecting.
/// </summary>
internal sealed class ControlPlaneLink(DaemonSettings settings, ProcessTable processes, NookDisk disk, TimeProvider time, ILogger<ControlPlaneLink> logger) : IDisposable
{
    private const int MaxConcurrentUploads = 64;

    // The exit an upload reports for a process this daemon doesn't know (daemon.proto, ProcessExited).
    private const int UnknownExitCode = -1;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan DiskReportInterval = TimeSpan.FromSeconds(30);

    // A connection that lasted this long resets the backoff.
    private static readonly TimeSpan StableConnection = TimeSpan.FromSeconds(10);

    private static readonly string Version =
        typeof(ControlPlaneLink).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    // Bounds concurrent uploads; every upload and input stream ends before its connection does.
    private readonly SemaphoreSlim _uploadSlots = new SemaphoreSlim(MaxConcurrentUploads);

    /// <summary>Connects, and reconnects with backoff, until <paramref name="ct"/> is cancelled.</summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using GrpcChannel channel = GrpcChannel.ForAddress(settings.ControlPlaneUrl, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                // Detects a dead connection while the control stream is idle.
                KeepAlivePingDelay = TimeSpan.FromSeconds(20),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
                EnableMultipleHttp2Connections = true,
            },
        });
        ControlPlane.ControlPlaneClient client = new ControlPlane.ControlPlaneClient(channel);

        int failures = 0;
        while (true)
        {
            long startedAt = time.GetTimestamp();
            bool reconnectNow;
            Exception? failure = null;
            try
            {
                reconnectNow = await RunConnectionAsync(client, ct);
            }
            catch (Exception exception) when (exception is RpcException or HttpRequestException)
            {
                // Shutting down cancels the call too; that is not a connection failure.
                ct.ThrowIfCancellationRequested();
                reconnectNow = false;
                failure = exception;
            }

            ct.ThrowIfCancellationRequested();
            if (reconnectNow)
            {
                Log.ReconnectRequested(logger);
                failures = 0;
                continue;
            }

            failures = time.GetElapsedTime(startedAt) >= StableConnection ? 0 : failures + 1;
            TimeSpan delay = RetryDelay(failures);
            Log.ConnectionLost(logger, failure, delay);
            await Task.Delay(delay, time, ct);
        }
    }

    // Returns true when the control plane asked for an immediate reconnect.
    private async Task<bool> RunConnectionAsync(ControlPlane.ControlPlaneClient client, CancellationToken ct)
    {
        using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using AsyncDuplexStreamingCall<DaemonEvent, DaemonInstruction> call = client.Connect(Authorization(), cancellationToken: connection.Token);

        await call.RequestStream.WriteAsync(new DaemonEvent { Hello = Hello() }, connection.Token);
        foreach (ProcessExited exited in processes.Exited())
        {
            await call.RequestStream.WriteAsync(new DaemonEvent { ProcessExited = exited }, connection.Token);
        }

        Log.Connected(logger);
        List<Task> uploads = [];
        Task sendingEvents = SendEventsAsync(call.RequestStream, connection.Token);
        try
        {
            await foreach (DaemonInstruction instruction in call.ResponseStream.ReadAllAsync(connection.Token))
            {
                switch (instruction.InstructionCase)
                {
                    case DaemonInstruction.InstructionOneofCase.StartProcess:
                        await processes.StartAsync(instruction.StartProcess);
                        if (instruction.StartProcess.InputStreamed)
                        {
                            uploads.RemoveAll(upload => upload.IsCompleted);
                            uploads.Add(ReadInputAsync(client, instruction.StartProcess.ProcessId, connection.Token));
                        }

                        break;
                    case DaemonInstruction.InstructionOneofCase.StopProcess:
                        processes.Stop(instruction.StopProcess.ProcessId);
                        break;
                    case DaemonInstruction.InstructionOneofCase.SendInput:
                        await processes.SendInputAsync(instruction.SendInput.ProcessId, instruction.SendInput.Data.Memory, connection.Token);
                        break;
                    case DaemonInstruction.InstructionOneofCase.WatchOutput:
                        uploads.RemoveAll(upload => upload.IsCompleted);
                        uploads.Add(UploadAsync(client, instruction.WatchOutput, connection.Token));
                        break;
                    case DaemonInstruction.InstructionOneofCase.Reconnect:
                        return true;
                    case DaemonInstruction.InstructionOneofCase.None:
                        // An instruction this daemon doesn't know, from a newer control plane.
                        break;
                }
            }

            return false;
        }
        finally
        {
            // Ends the exit sender and every upload of this connection; the control plane watches
            // again after the next connection.
            await connection.CancelAsync();
            await Task.WhenAll(uploads.Append(sendingEvents));
        }
    }

    // Sends exits as they happen, and the disk's usage on connecting, every 30 seconds, and as soon as
    // it fills. One loop, because a call's request stream takes one write at a time.
    private async Task SendEventsAsync(IClientStreamWriter<DaemonEvent> stream, CancellationToken ct)
    {
        try
        {
            Task<bool> exits = processes.Exits.WaitToReadAsync(ct).AsTask();
            Task diskDue = Task.CompletedTask;
            while (true)
            {
                if (diskDue.IsCompleted)
                {
                    disk.Reserve();
                    await stream.WriteAsync(new DaemonEvent { DiskUsage = disk.Measure() }, ct);
                    diskDue = Task.WhenAny(Task.Delay(DiskReportInterval, time, ct), disk.Full);
                }

                if (exits.IsCompleted)
                {
                    bool more = await exits;
                    if (!more)
                    {
                        return;
                    }

                    while (processes.Exits.TryRead(out ProcessExited? exited))
                    {
                        await stream.WriteAsync(new DaemonEvent { ProcessExited = exited }, ct);
                    }

                    exits = processes.Exits.WaitToReadAsync(ct).AsTask();
                }

                await Task.WhenAny(exits, diskDue);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or RpcException or InvalidOperationException && ct.IsCancellationRequested)
        {
            // The connection ended; exits are sent again on the next one.
        }
    }

    public void Dispose()
    {
        _uploadSlots.Dispose();
    }

    private async Task UploadAsync(ControlPlane.ControlPlaneClient client, WatchOutput watch, CancellationToken ct)
    {
        try
        {
            await _uploadSlots.WaitAsync(ct);
            try
            {
                using AsyncClientStreamingCall<OutputUploadMessage, OutputUploadResult> call = client.UploadOutput(Authorization(), cancellationToken: ct);
                OutputUploadHeader header = new OutputUploadHeader
                {
                    NookId = settings.NookId.ToString("D", CultureInfo.InvariantCulture),
                    WatchId = watch.WatchId,
                };
                await call.RequestStream.WriteAsync(new OutputUploadMessage { Header = header }, ct);

                // A process this daemon doesn't know ended with an earlier daemon, so its exit is unknown.
                int exitCode = UnknownExitCode;
                NookProcess? process = processes.Find(watch.ProcessId);
                if (process is not null)
                {
                    await foreach (OutputChunk chunk in process.Output.ReadAsync(watch.FromOffset, ct))
                    {
                        ProcessOutput output = new ProcessOutput
                        {
                            ProcessId = process.Id,
                            Offset = chunk.Offset,
                            Channel = chunk.Channel,

                            // Each chunk owns its buffer, so wrapping it without a copy is safe.
                            Data = UnsafeByteOperations.UnsafeWrap(chunk.Data),
                        };
                        await call.RequestStream.WriteAsync(new OutputUploadMessage { Output = output }, ct);
                        process.Output.MarkDelivered(chunk.Offset + chunk.Data.Length);
                    }

                    // The output is complete once the process has exited, so its exit code is moments away.
                    exitCode = await process.Exited;
                }

                ProcessExited exited = new ProcessExited { ProcessId = watch.ProcessId, ExitCode = exitCode };
                await call.RequestStream.WriteAsync(new OutputUploadMessage { Exited = exited }, ct);
                await call.RequestStream.CompleteAsync();
                await call.ResponseAsync;
            }
            finally
            {
                _uploadSlots.Release();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The connection ended.
        }
        catch (RpcException exception)
        {
            // The watcher left, or the connection broke; either way the watch is over.
            Log.WatchEnded(logger, exception, watch.WatchId);
        }
    }

    // Feeds a process's standard input from its own stream, then closes it, however the stream ends.
    private async Task ReadInputAsync(ControlPlane.ControlPlaneClient client, string processId, CancellationToken ct)
    {
        NookProcess? process = processes.Find(processId);
        if (process is null)
        {
            return;
        }

        try
        {
            InputRequest request = new InputRequest { NookId = settings.NookId.ToString("D", CultureInfo.InvariantCulture), ProcessId = processId };
            using AsyncServerStreamingCall<InputChunk> call = client.ReadInput(request, Authorization(), cancellationToken: ct);
            await foreach (InputChunk chunk in call.ResponseStream.ReadAllAsync(ct))
            {
                await process.SendInputAsync(chunk.Data.Memory, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The connection ended; the input is cut short.
        }
        catch (RpcException exception)
        {
            Log.InputEnded(logger, exception, processId);
        }
        finally
        {
            process.CompleteInput();
        }
    }

    private Hello Hello()
    {
        Hello hello = new Hello
        {
            NookId = settings.NookId.ToString("D", CultureInfo.InvariantCulture),
            DaemonVersion = Version,
        };
        hello.RunningProcesses.AddRange(processes.Running());
        return hello;
    }

    private Metadata Authorization()
    {
        return new Metadata { { "authorization", "Bearer " + settings.Token } };
    }

    private static TimeSpan RetryDelay(int failures)
    {
        double seconds = Math.Min(MaxRetryDelay.TotalSeconds, 0.5 * Math.Pow(2, Math.Min(failures, 10)));
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(0, 500));
    }
}
