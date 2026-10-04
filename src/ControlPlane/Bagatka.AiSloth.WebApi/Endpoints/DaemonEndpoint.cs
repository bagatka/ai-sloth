using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Google.Protobuf;
using Grpc.Core;
using Wire = Bagatka.AiSloth.DaemonProtocol.V1;

namespace Bagatka.AiSloth.WebApi.Endpoints;

/// <summary>
/// The endpoint every nook's daemon dials (<c>src/Daemon/Bagatka.AiSloth.DaemonProtocol/daemon.proto</c>).
/// It translates the wire protocol to <see cref="INookDaemonsApi"/> and back; Nooks owns every rule.
/// </summary>
internal sealed class DaemonEndpoint(INookDaemonsApi nooks) : Wire.ControlPlane.ControlPlaneBase
{
    public override async Task Connect(IAsyncStreamReader<Wire.DaemonEvent> requestStream, IServerStreamWriter<Wire.DaemonInstruction> responseStream, ServerCallContext context)
    {
        CancellationToken ct = context.CancellationToken;
        bool opened = await requestStream.MoveNext(ct);
        Wire.Hello? hello = opened ? requestStream.Current.Hello : null;
        if (hello is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The first event must be Hello."));
        }

        ConnectDaemon command = new ConnectDaemon(
            NookId.From(GrpcCalls.ParseId(hello.NookId)),
            GrpcCalls.BearerToken(context),
            hello.DaemonVersion,
            hello.RunningProcesses.Select(running => new RunningProcess(ProcessId.From(GrpcCalls.ParseId(running.ProcessId)), running.OutputLength)).ToList());
        Result<IAsyncEnumerable<DaemonInstruction>> connected = await nooks.ConnectAsync(Actor.Anonymous, command, ReportsAsync(requestStream, ct), ct);
        if (connected.Failed)
        {
            throw GrpcCalls.Rejection(connected.Error);
        }

        try
        {
            await foreach (DaemonInstruction instruction in connected.Output.WithCancellation(ct))
            {
                await responseStream.WriteAsync(ToWire(instruction), ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The daemon went away; it reconnects.
        }
    }

    public override async Task<Wire.OutputUploadResult> UploadOutput(IAsyncStreamReader<Wire.OutputUploadMessage> requestStream, ServerCallContext context)
    {
        CancellationToken ct = context.CancellationToken;
        bool opened = await requestStream.MoveNext(ct);
        Wire.OutputUploadHeader? header = opened ? requestStream.Current.Header : null;
        if (header is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The first message must be the header."));
        }

        OutputUpload upload = new OutputUpload(NookId.From(GrpcCalls.ParseId(header.NookId)), GrpcCalls.BearerToken(context), WatchId.From(GrpcCalls.ParseId(header.WatchId)));
        Result accepted = await nooks.AcceptOutputAsync(Actor.Anonymous, upload, UploadedAsync(requestStream, ct), ct);
        if (accepted.Failed)
        {
            throw GrpcCalls.Rejection(accepted.Error);
        }

        return new Wire.OutputUploadResult();
    }

    // The reports after Hello. The stream ends however the daemon leaves: reports carry no state a
    // broken connection could leave half done.
    private static async IAsyncEnumerable<DaemonReport> ReportsAsync(IAsyncStreamReader<Wire.DaemonEvent> events, [EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            bool more;
            try
            {
                more = await events.MoveNext(ct);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                more = false;
            }

            if (!more)
            {
                yield break;
            }

            switch (events.Current.EventCase)
            {
                case Wire.DaemonEvent.EventOneofCase.ProcessExited:
                    Wire.ProcessExited exited = events.Current.ProcessExited;
                    yield return new DaemonReport(new ProcessExited(ProcessId.From(GrpcCalls.ParseId(exited.ProcessId)), exited.ExitCode));
                    break;
                case Wire.DaemonEvent.EventOneofCase.DiskUsage:
                    yield return new DaemonReport(new DiskUsage(events.Current.DiskUsage.TotalBytes, events.Current.DiskUsage.AvailableBytes));
                    break;
                case Wire.DaemonEvent.EventOneofCase.Hello or Wire.DaemonEvent.EventOneofCase.None:
                    // A repeated hello, or an event from a newer daemon: nothing to report.
                    break;
            }
        }
    }

    // An upload's chunks, then the exit. Unlike reports, a daemon leaving mid-upload throws, so the
    // watch knows its output is incomplete.
    private static async IAsyncEnumerable<ProcessEvent> UploadedAsync(IAsyncStreamReader<Wire.OutputUploadMessage> messages, [EnumeratorCancellation] CancellationToken ct)
    {
        await foreach (Wire.OutputUploadMessage message in messages.ReadAllAsync(ct))
        {
            switch (message.PartCase)
            {
                case Wire.OutputUploadMessage.PartOneofCase.Output:
                    Wire.ProcessOutput output = message.Output;
                    OutputChannel channel = output.Channel switch
                    {
                        Wire.OutputChannel.StandardError => OutputChannel.StandardError,
                        Wire.OutputChannel.StandardOutput or Wire.OutputChannel.Unspecified => OutputChannel.StandardOutput,
                    };
                    yield return new ProcessEvent(new ProcessOutput(ProcessId.From(GrpcCalls.ParseId(output.ProcessId)), output.Offset, channel, output.Data.Memory));
                    break;
                case Wire.OutputUploadMessage.PartOneofCase.Exited:
                    Wire.ProcessExited exited = message.Exited;
                    yield return new ProcessEvent(new ProcessExited(ProcessId.From(GrpcCalls.ParseId(exited.ProcessId)), exited.ExitCode));
                    break;
                case Wire.OutputUploadMessage.PartOneofCase.Header or Wire.OutputUploadMessage.PartOneofCase.None:
                    throw new RpcException(new Status(StatusCode.InvalidArgument, "Only output and the exit follow the header."));
            }
        }
    }

    private static Wire.StartProcess ToWire(StartProcessInstruction start)
    {
        Wire.StartProcess wire = new Wire.StartProcess
        {
            ProcessId = GrpcCalls.FormatId(start.ProcessId.Value),
            Command = start.Command,
            Arguments = { start.Arguments },
            WorkingDirectory = start.WorkingDirectory ?? string.Empty,
            Retention = start.Retention switch
            {
                OutputRetention.Recent => Wire.OutputRetention.Recent,
                OutputRetention.Complete => Wire.OutputRetention.Complete,
            },
        };
        foreach (KeyValuePair<string, string> variable in start.Environment)
        {
            wire.Environment.Add(variable.Key, variable.Value);
        }

        return wire;
    }

    private static Wire.DaemonInstruction ToWire(DaemonInstruction instruction)
    {
        return instruction switch
        {
            StartProcessInstruction start => new Wire.DaemonInstruction { StartProcess = ToWire(start) },
            StopProcessInstruction stop => new Wire.DaemonInstruction
            {
                StopProcess = new Wire.StopProcess { ProcessId = GrpcCalls.FormatId(stop.ProcessId.Value) },
            },
            SendInputInstruction input => new Wire.DaemonInstruction
            {
                SendInput = new Wire.SendInput { ProcessId = GrpcCalls.FormatId(input.ProcessId.Value), Data = ByteString.CopyFrom(input.Data.Span) },
            },
            WatchOutputInstruction watch => new Wire.DaemonInstruction
            {
                WatchOutput = new Wire.WatchOutput { WatchId = GrpcCalls.FormatId(watch.WatchId.Value), ProcessId = GrpcCalls.FormatId(watch.ProcessId.Value), FromOffset = watch.FromOffset },
            },
            ReconnectInstruction => new Wire.DaemonInstruction { Reconnect = new Wire.Reconnect() },
        };
    }
}
