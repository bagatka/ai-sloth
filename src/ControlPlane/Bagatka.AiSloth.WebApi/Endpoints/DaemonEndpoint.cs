using System;
using System.Collections.Generic;
using System.Globalization;
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
        if (!await requestStream.MoveNext(ct) || requestStream.Current.Hello is not Wire.Hello hello)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The first event must be Hello."));
        }

        ConnectDaemon command = new ConnectDaemon(
            NookId.From(ParseId(hello.NookId)),
            Token(context),
            hello.DaemonVersion,
            hello.RunningProcesses.Select(running => new RunningProcess(ProcessId.From(ParseId(running.ProcessId)), running.OutputLength)).ToList());
        Result<IAsyncEnumerable<DaemonInstruction>> connected = await nooks.ConnectAsync(Actor.Anonymous, command, ReportsAsync(requestStream, ct), ct);
        if (!connected.TryGetValue(out IAsyncEnumerable<DaemonInstruction>? instructions, out Error? rejected))
        {
            throw Rejection(rejected);
        }

        try
        {
            await foreach (DaemonInstruction instruction in instructions.WithCancellation(ct))
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
        if (!await requestStream.MoveNext(ct) || requestStream.Current.Header is not Wire.OutputUploadHeader header)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The first message must be the header."));
        }

        OutputUpload upload = new OutputUpload(NookId.From(ParseId(header.NookId)), Token(context), WatchId.From(ParseId(header.WatchId)));
        Result accepted = await nooks.AcceptOutputAsync(Actor.Anonymous, upload, OutputAsync(requestStream, ct), ct);
        if (accepted.IsError(out Error? rejected))
        {
            throw Rejection(rejected);
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
                    yield return new DaemonReport(new ProcessExited(ProcessId.From(ParseId(exited.ProcessId)), exited.ExitCode));
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

    // An upload's chunks. Unlike reports, a daemon leaving mid-upload throws, so the watch knows its
    // output is incomplete.
    private static async IAsyncEnumerable<ProcessOutput> OutputAsync(IAsyncStreamReader<Wire.OutputUploadMessage> messages, [EnumeratorCancellation] CancellationToken ct)
    {
        while (await messages.MoveNext(ct))
        {
            Wire.ProcessOutput output = messages.Current.Output
                ?? throw new RpcException(new Status(StatusCode.InvalidArgument, "Only output follows the header."));
            OutputChannel channel = output.Channel switch
            {
                Wire.OutputChannel.StandardError => OutputChannel.StandardError,
                Wire.OutputChannel.StandardOutput or Wire.OutputChannel.Unspecified => OutputChannel.StandardOutput,
            };
            yield return new ProcessOutput(ProcessId.From(ParseId(output.ProcessId)), output.Offset, channel, output.Data.Memory);
        }
    }

    private static Wire.DaemonInstruction ToWire(DaemonInstruction instruction)
    {
        return instruction switch
        {
            StartProcessInstruction start => new Wire.DaemonInstruction
            {
                StartProcess = new Wire.StartProcess
                {
                    ProcessId = FormatId(start.ProcessId.Value),
                    Command = start.Command,
                    Arguments = { start.Arguments },
                    WorkingDirectory = start.WorkingDirectory ?? string.Empty,
                    Retention = start.Retention switch
                    {
                        OutputRetention.Recent => Wire.OutputRetention.Recent,
                        OutputRetention.Complete => Wire.OutputRetention.Complete,
                    },
                },
            },
            StopProcessInstruction stop => new Wire.DaemonInstruction
            {
                StopProcess = new Wire.StopProcess { ProcessId = FormatId(stop.ProcessId.Value) },
            },
            SendInputInstruction input => new Wire.DaemonInstruction
            {
                SendInput = new Wire.SendInput { ProcessId = FormatId(input.ProcessId.Value), Data = ByteString.CopyFrom(input.Data.Span) },
            },
            WatchOutputInstruction watch => new Wire.DaemonInstruction
            {
                WatchOutput = new Wire.WatchOutput { WatchId = FormatId(watch.WatchId.Value), ProcessId = FormatId(watch.ProcessId.Value), FromOffset = watch.FromOffset },
            },
            ReconnectInstruction => new Wire.DaemonInstruction { Reconnect = new Wire.Reconnect() },
        };
    }

    // The daemon's token, from the authorization metadata every call carries.
    private static string Token(ServerCallContext context)
    {
        const string Scheme = "Bearer ";
        string? authorization = context.RequestHeaders.GetValue("authorization");
        return authorization is not null && authorization.StartsWith(Scheme, StringComparison.Ordinal)
            ? authorization[Scheme.Length..]
            : throw new RpcException(new Status(StatusCode.Unauthenticated, "Calls carry the nook's token as a bearer token."));
    }

    private static Guid ParseId(string value)
    {
        return Guid.TryParse(value, CultureInfo.InvariantCulture, out Guid id)
            ? id
            : throw new RpcException(new Status(StatusCode.InvalidArgument, "IDs are UUIDs."));
    }

    private static string FormatId(Guid value)
    {
        return value.ToString("D", CultureInfo.InvariantCulture);
    }

    private static RpcException Rejection(Error error)
    {
        StatusCode status = error.Kind switch
        {
            ErrorKind.Validation => StatusCode.InvalidArgument,
            ErrorKind.Unauthorized => StatusCode.Unauthenticated,
            ErrorKind.Forbidden => StatusCode.PermissionDenied,
            ErrorKind.NotFound => StatusCode.NotFound,
            ErrorKind.Conflict => StatusCode.FailedPrecondition,
        };
        return new RpcException(new Status(status, error.Message));
    }
}
