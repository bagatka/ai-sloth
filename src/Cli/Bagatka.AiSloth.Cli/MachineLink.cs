using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Reflection;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Bagatka.AiSloth.MachineProtocol.V1;
using Bagatka.Sandboxing;
using Bagatka.Sandboxing.Remote;
using Bagatka.Sandboxing.Remote.V1;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.Cli;

/// <summary>
/// The machine's side of <c>machine.proto</c>: keeps the connection open and runs the control plane's
/// sandbox calls on the local provider, several at a time. Sandboxes never depend on the connection;
/// losing it only means reconnecting.
/// </summary>
internal sealed class MachineLink(MachineCredential credential, ISandboxProvider local, TimeProvider time, ILogger<MachineLink> logger)
{
    // Calls are short provider operations; the bound keeps a flood from opening unbounded work.
    private const int MaxConcurrentCalls = 32;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromSeconds(30);

    // A connection that lasted this long resets the backoff.
    private static readonly TimeSpan StableConnection = TimeSpan.FromSeconds(10);

    private static readonly string Version =
        typeof(MachineLink).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "unknown";

    /// <summary>
    /// Trades a one-time code for the machine's credential. Throws <see cref="RpcException"/> with
    /// <see cref="StatusCode.Unauthenticated"/> for an unknown, used, or expired code.
    /// </summary>
    public static async Task<MachineCredential> RegisterAsync(Uri controlPlaneUrl, string code, CancellationToken ct)
    {
        using GrpcChannel channel = GrpcChannel.ForAddress(controlPlaneUrl);
        Machines.MachinesClient client = new Machines.MachinesClient(channel);
        RegisterResponse response = await client.RegisterAsync(new RegisterRequest { Code = code, SlothVersion = Version }, cancellationToken: ct);
        return new MachineCredential(controlPlaneUrl, Guid.Parse(response.MachineId, CultureInfo.InvariantCulture), response.Token);
    }

    /// <summary>
    /// Connects, and reconnects with backoff, until the control plane rejects the credential, which
    /// means the machine was removed, or until <paramref name="ct"/> is cancelled.
    /// </summary>
    public async Task RunAsync(CancellationToken ct)
    {
        using GrpcChannel channel = GrpcChannel.ForAddress(credential.ControlPlaneUrl, new GrpcChannelOptions
        {
            HttpHandler = new SocketsHttpHandler
            {
                // Detects a dead connection while no calls flow.
                KeepAlivePingDelay = TimeSpan.FromSeconds(20),
                KeepAlivePingTimeout = TimeSpan.FromSeconds(10),
                KeepAlivePingPolicy = HttpKeepAlivePingPolicy.Always,
                PooledConnectionIdleTimeout = Timeout.InfiniteTimeSpan,
            },
        });
        Machines.MachinesClient client = new Machines.MachinesClient(channel);

        int failures = 0;
        while (true)
        {
            long startedAt = time.GetTimestamp();
            Exception? failure = null;
            try
            {
                await RunConnectionAsync(client, ct);
            }
            catch (RpcException exception) when (exception.StatusCode == StatusCode.Unauthenticated)
            {
                return;
            }
            catch (Exception exception) when (exception is RpcException or HttpRequestException)
            {
                // Shutting down cancels the call too; that is not a connection failure.
                ct.ThrowIfCancellationRequested();
                failure = exception;
            }

            ct.ThrowIfCancellationRequested();
            failures = time.GetElapsedTime(startedAt) >= StableConnection ? 0 : failures + 1;
            TimeSpan delay = RetryDelay(failures);
            Log.ConnectionLost(logger, failure, delay);
            await Task.Delay(delay, time, ct);
        }
    }

    private async Task RunConnectionAsync(Machines.MachinesClient client, CancellationToken ct)
    {
        using CancellationTokenSource connection = CancellationTokenSource.CreateLinkedTokenSource(ct);
        using AsyncDuplexStreamingCall<MachineMessage, SandboxCall> call = client.Connect(Authorization(), cancellationToken: connection.Token);
        Hello hello = new Hello { MachineId = credential.MachineId.ToString("D", CultureInfo.InvariantCulture), SlothVersion = Version };
        await call.RequestStream.WriteAsync(new MachineMessage { Hello = hello }, connection.Token);
        Log.Connected(logger);

        // At most MaxConcurrentCalls run, and a call ends once its result is queued, so the queue
        // never holds more.
        Channel<SandboxCallResult> results = Channel.CreateBounded<SandboxCallResult>(
            new BoundedChannelOptions(MaxConcurrentCalls) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        Task sending = SendResultsAsync(call.RequestStream, results.Reader, connection.Token);
        List<Task> running = [];
        try
        {
            await foreach (SandboxCall sandboxCall in call.ResponseStream.ReadAllAsync(connection.Token))
            {
                running.RemoveAll(task => task.IsCompleted);
                if (running.Count == MaxConcurrentCalls)
                {
                    await Task.WhenAny(running);
                    running.RemoveAll(task => task.IsCompleted);
                }

                running.Add(ExecuteAsync(sandboxCall, results.Writer, connection.Token));
            }
        }
        finally
        {
            // The control plane fails whatever this connection left unanswered; its callers retry.
            await connection.CancelAsync();
            await Task.WhenAll(running);
            results.Writer.TryComplete();
            await sending;
        }
    }

    private async Task ExecuteAsync(SandboxCall sandboxCall, ChannelWriter<SandboxCallResult> results, CancellationToken ct)
    {
        try
        {
            SandboxCallResult result = await SandboxCalls.ExecuteAsync(local, sandboxCall, ct);
            await results.WriteAsync(result, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The connection ended.
        }
    }

    // One loop, because a call's request stream takes one write at a time.
    private static async Task SendResultsAsync(IClientStreamWriter<MachineMessage> stream, ChannelReader<SandboxCallResult> results, CancellationToken ct)
    {
        try
        {
            await foreach (SandboxCallResult result in results.ReadAllAsync(ct))
            {
                await stream.WriteAsync(new MachineMessage { Result = result }, ct);
            }
        }
        catch (Exception exception) when (exception is OperationCanceledException or RpcException or InvalidOperationException && ct.IsCancellationRequested)
        {
            // The connection ended.
        }
    }

    private Metadata Authorization()
    {
        return new Metadata { { "authorization", "Bearer " + credential.Token } };
    }

    private static TimeSpan RetryDelay(int failures)
    {
        double seconds = Math.Min(MaxRetryDelay.TotalSeconds, 0.5 * Math.Pow(2, Math.Min(failures, 10)));
        return TimeSpan.FromSeconds(seconds) + TimeSpan.FromMilliseconds(RandomNumberGenerator.GetInt32(0, 500));
    }
}
