using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Machines.Contracts;
using Bagatka.Foundation;
using Grpc.Core;
using Calls = Bagatka.Sandboxing.Remote.V1;
using Wire = Bagatka.AiSloth.MachineProtocol.V1;

namespace Bagatka.AiSloth.WebApi.Endpoints;

/// <summary>
/// The endpoint <c>sloth machine run</c> dials (<c>src/Cli/Bagatka.AiSloth.MachineProtocol/machine.proto</c>).
/// It translates the wire protocol to <see cref="IMachineConnectionsApi"/>; Machines owns every rule.
/// </summary>
internal sealed class MachineEndpoint(IMachineConnectionsApi machines) : Wire.Machines.MachinesBase
{
    public override async Task<Wire.RegisterResponse> Register(Wire.RegisterRequest request, ServerCallContext context)
    {
        Result<MachineCredential> registered = await machines.RegisterAsync(Actor.Anonymous, new RegisterMachine(request.Code, request.SlothVersion), context.CancellationToken);
        if (registered.Failed)
        {
            throw GrpcCalls.Rejection(registered.Error);
        }

        MachineCredential credential = registered.Output;
        return new Wire.RegisterResponse { MachineId = GrpcCalls.FormatId(credential.MachineId.Value), Token = credential.Token };
    }

    public override async Task Connect(IAsyncStreamReader<Wire.MachineMessage> requestStream, IServerStreamWriter<Calls.SandboxCall> responseStream, ServerCallContext context)
    {
        CancellationToken ct = context.CancellationToken;
        bool opened = await requestStream.MoveNext(ct);
        Wire.Hello? hello = opened ? requestStream.Current.Hello : null;
        if (hello is null)
        {
            throw new RpcException(new Status(StatusCode.InvalidArgument, "The first message must be Hello."));
        }

        ConnectMachine command = new ConnectMachine(MachineId.From(GrpcCalls.ParseId(hello.MachineId)), GrpcCalls.BearerToken(context), hello.SlothVersion);
        Result<IAsyncEnumerable<Calls.SandboxCall>> connected = await machines.ConnectAsync(Actor.Anonymous, command, ResultsAsync(requestStream, ct), ct);
        if (connected.Failed)
        {
            throw GrpcCalls.Rejection(connected.Error);
        }

        try
        {
            await foreach (Calls.SandboxCall call in connected.Output.WithCancellation(ct))
            {
                await responseStream.WriteAsync(call, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The machine went away; it reconnects.
        }
    }

    // The results after Hello, until the machine leaves however it leaves.
    private static async IAsyncEnumerable<Calls.SandboxCallResult> ResultsAsync(IAsyncStreamReader<Wire.MachineMessage> messages, [EnumeratorCancellation] CancellationToken ct)
    {
        while (true)
        {
            bool more;
            try
            {
                more = await messages.MoveNext(ct);
            }
            catch (Exception exception) when (exception is IOException or OperationCanceledException)
            {
                more = false;
            }

            if (!more)
            {
                yield break;
            }

            switch (messages.Current.MessageCase)
            {
                case Wire.MachineMessage.MessageOneofCase.Result:
                    yield return messages.Current.Result;
                    break;
                case Wire.MachineMessage.MessageOneofCase.Hello or Wire.MachineMessage.MessageOneofCase.None:
                    // A repeated hello, or a message from a newer sloth: nothing to deliver.
                    break;
            }
        }
    }
}
