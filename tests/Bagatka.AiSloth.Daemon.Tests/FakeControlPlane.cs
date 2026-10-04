using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.DaemonProtocol.V1;
using Grpc.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Daemon.Tests;

/// <summary>
/// The control plane's daemon endpoint as the daemon sees it: a real gRPC server on a free local
/// port. Tests receive each connection the daemon makes and each upload it sends.
/// </summary>
internal sealed class FakeControlPlane : IAsyncDisposable
{
    private readonly WebApplication _app;

    private FakeControlPlane(WebApplication app, DaemonEndpoint endpoint)
    {
        _app = app;
        Endpoint = endpoint;
        IServerAddressesFeature addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel reported no addresses.");
        Url = new Uri(addresses.Addresses.Single());
    }

    public Uri Url { get; }

    public DaemonEndpoint Endpoint { get; }

    public static async Task<FakeControlPlane> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
            kestrel.Listen(IPAddress.Loopback, 0, listen => listen.Protocols = HttpProtocols.Http2));
        DaemonEndpoint endpoint = new DaemonEndpoint();
        builder.Services.AddSingleton(endpoint);
        builder.Services.AddGrpc();

        WebApplication app = builder.Build();
        app.MapGrpcService<DaemonEndpoint>();
        await app.StartAsync();
        return new FakeControlPlane(app, endpoint);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    /// <summary>The fake endpoint: hands connections and uploads to the test.</summary>
    internal sealed class DaemonEndpoint : ControlPlane.ControlPlaneBase
    {
        private readonly Channel<Connection> _connections = Channel.CreateUnbounded<Connection>();
        private readonly ConcurrentDictionary<string, Channel<ProcessOutput>> _uploads = new ConcurrentDictionary<string, Channel<ProcessOutput>>(StringComparer.Ordinal);

        /// <summary>The daemon's next connection.</summary>
        public async Task<Connection> NextConnectionAsync(CancellationToken ct)
        {
            return await _connections.Reader.ReadAsync(ct);
        }

        /// <summary>The output uploaded for a watch, as it arrives; completes when the upload ends.</summary>
        public ChannelReader<ProcessOutput> Upload(string watchId)
        {
            return _uploads.GetOrAdd(watchId, _ => Channel.CreateUnbounded<ProcessOutput>()).Reader;
        }

        public override async Task Connect(IAsyncStreamReader<DaemonEvent> requestStream, IServerStreamWriter<DaemonInstruction> responseStream, ServerCallContext context)
        {
            if (!await requestStream.MoveNext(context.CancellationToken) || requestStream.Current.EventCase != DaemonEvent.EventOneofCase.Hello)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "The first event must be Hello."));
            }

            using Connection connection = new Connection(requestStream.Current.Hello, context.RequestHeaders.GetValue("authorization"));
            await _connections.Writer.WriteAsync(connection, context.CancellationToken);
            using CancellationTokenSource ended = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, connection.Dropped);
            Task reading = connection.ReadEventsAsync(requestStream, ended.Token);
            try
            {
                await foreach (DaemonInstruction instruction in connection.Instructions.Reader.ReadAllAsync(ended.Token))
                {
                    await responseStream.WriteAsync(instruction, ended.Token);
                }
            }
            catch (OperationCanceledException) when (ended.IsCancellationRequested)
            {
                // The test dropped the connection, or the daemon went away.
            }

            await reading;
            if (connection.Dropped.IsCancellationRequested)
            {
                throw new RpcException(new Status(StatusCode.Unavailable, "Dropped by the test."));
            }
        }

        public override async Task<OutputUploadResult> UploadOutput(IAsyncStreamReader<OutputUploadMessage> requestStream, ServerCallContext context)
        {
            if (!await requestStream.MoveNext(context.CancellationToken) || requestStream.Current.PartCase != OutputUploadMessage.PartOneofCase.Header)
            {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "The first message must be the header."));
            }

            ChannelWriter<ProcessOutput> upload = _uploads.GetOrAdd(requestStream.Current.Header.WatchId, _ => Channel.CreateUnbounded<ProcessOutput>()).Writer;
            await foreach (OutputUploadMessage message in requestStream.ReadAllAsync(context.CancellationToken))
            {
                await upload.WriteAsync(message.Output, context.CancellationToken);
            }

            upload.TryComplete();
            return new OutputUploadResult();
        }
    }

    /// <summary>One control stream the daemon opened.</summary>
    internal sealed class Connection(Hello hello, string? authorization) : IDisposable
    {
        private readonly CancellationTokenSource _dropped = new CancellationTokenSource();

        public Hello Hello { get; } = hello;

        public string? Authorization { get; } = authorization;

        /// <summary>Events the daemon sent after its hello.</summary>
        public Channel<DaemonEvent> Events { get; } = Channel.CreateUnbounded<DaemonEvent>();

        /// <summary>Write here to instruct the daemon.</summary>
        public Channel<DaemonInstruction> Instructions { get; } = Channel.CreateUnbounded<DaemonInstruction>();

        public CancellationToken Dropped => _dropped.Token;

        /// <summary>Breaks the connection, as a network failure would.</summary>
        public void Drop()
        {
            _dropped.Cancel();
        }

        public void Dispose()
        {
            _dropped.Dispose();
        }

        public async Task ReadEventsAsync(IAsyncStreamReader<DaemonEvent> stream, CancellationToken ct)
        {
            try
            {
                await foreach (DaemonEvent daemonEvent in stream.ReadAllAsync(ct))
                {
                    await Events.Writer.WriteAsync(daemonEvent, ct);
                }
            }
            catch (Exception exception) when (exception is OperationCanceledException or IOException or RpcException)
            {
                // The connection ended.
            }
        }
    }
}
