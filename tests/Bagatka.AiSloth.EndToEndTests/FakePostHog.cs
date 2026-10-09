using System;
using System.Collections.Concurrent;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A PostHog project at the HTTP boundary: it keeps the events the host captures, unzipped, and counts
/// the OTLP requests its logs, traces, and metrics come in, by path, checking each carries the token.
/// </summary>
internal sealed class FakePostHog : IAsyncDisposable
{
    /// <summary>The project token the host is given.</summary>
    public const string ProjectToken = "phc_e2e";

    private readonly WebApplication _app;
    private readonly ConcurrentDictionary<string, TaskCompletionSource> _received = new ConcurrentDictionary<string, TaskCompletionSource>(StringComparer.Ordinal);
    private TaskCompletionSource _nextBatch = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private FakePostHog(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Its address, as the host is given it.</summary>
    public Uri Url { get; private set; } = new Uri("http://localhost");

    /// <summary>Every event captured so far.</summary>
    public ConcurrentQueue<JsonElement> Events { get; } = new ConcurrentQueue<JsonElement>();

    public static async Task<FakePostHog> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        WebApplication app = builder.Build();
        FakePostHog postHog = new FakePostHog(app);
        app.MapPost("/batch/", postHog.BatchAsync);
        app.MapPost("/i/v1/{signal}", postHog.Otlp);
        await app.StartAsync();
        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        if (addresses is null)
        {
            throw new InvalidOperationException("Kestrel reported no addresses.");
        }

        postHog.Url = new Uri(addresses.Addresses.Single());
        return postHog;
    }

    /// <summary>Waits until something arrived at the path, such as <c>/i/v1/traces</c> or <c>/batch/</c>.</summary>
    public Task ReceivedAsync(string path, TimeSpan timeout)
    {
        return Signal(path).Task.WaitAsync(timeout);
    }

    /// <summary>Waits for an event of the name that matches, captured so far or within the timeout.</summary>
    public async Task<JsonElement> EventAsync(string name, Func<JsonElement, bool> matches, TimeSpan timeout)
    {
        using CancellationTokenSource deadline = new CancellationTokenSource(timeout);
        while (true)
        {
            Task nextBatch = Volatile.Read(ref _nextBatch).Task;
            foreach (JsonElement captured in Events)
            {
                if (string.Equals(captured.GetProperty("event").GetString(), name, StringComparison.Ordinal) && matches(captured))
                {
                    return captured;
                }
            }

            await nextBatch.WaitAsync(deadline.Token);
        }
    }

    /// <summary>Whether a captured event belongs to the workspace, by its ID as the host writes it.</summary>
    public static bool InWorkspace(JsonElement captured, string workspace)
    {
        JsonElement properties = captured.GetProperty("properties");
        bool grouped = properties.TryGetProperty("$groups", out JsonElement groups);
        return grouped && string.Equals(groups.GetProperty("workspace").GetString(), workspace, StringComparison.Ordinal);
    }

    public async ValueTask DisposeAsync()
    {
        await _app.DisposeAsync();
    }

    private async Task<IResult> BatchAsync(HttpRequest request, CancellationToken ct)
    {
        await using GZipStream unzipped = new GZipStream(request.Body, CompressionMode.Decompress);
        using JsonDocument body = await JsonDocument.ParseAsync(unzipped, cancellationToken: ct);
        if (!string.Equals(body.RootElement.GetProperty("api_key").GetString(), ProjectToken, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        foreach (JsonElement captured in body.RootElement.GetProperty("batch").EnumerateArray())
        {
            Events.Enqueue(captured.Clone());
        }

        Signal("/batch/").TrySetResult();
        Interlocked.Exchange(ref _nextBatch, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
        return Results.Ok();
    }

    private IResult Otlp(HttpRequest request, string signal)
    {
        if (!string.Equals(request.Headers.Authorization.ToString(), "Bearer " + ProjectToken, StringComparison.Ordinal))
        {
            return Results.Unauthorized();
        }

        Signal("/i/v1/" + signal).TrySetResult();
        return Results.Ok();
    }

    private TaskCompletionSource Signal(string path)
    {
        return _received.GetOrAdd(path, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }
}
