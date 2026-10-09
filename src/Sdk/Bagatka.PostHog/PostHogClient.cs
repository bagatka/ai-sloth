using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Bagatka.PostHog;

/// <summary>
/// Sends events to a PostHog project. Capturing never blocks: an event is written out at once and
/// waits in a queue of at most 10,000, which the client sends in the background every
/// <see cref="PostHogClientOptions.FlushInterval"/>, up to 1,000 events a request, gzipped. A request
/// PostHog fails or can't be reached for is sent again twice, a second and two seconds later; every
/// event carries its own UUID, so PostHog keeps one of each. Events lost on the way are reported to
/// <see cref="PostHogClientOptions.DeliveryFailed"/>. Disposing sends what is still queued, for up to
/// <see cref="PostHogClientOptions.ShutdownTimeout"/>. Every member is safe to call from any thread.
/// </summary>
public sealed class PostHogClient : IAsyncDisposable
{
    private const string LibraryName = "bagatka-posthog";
    private const int MaxQueued = 10_000;
    private const int MaxBatch = 1_000;
    private static readonly TimeSpan[] RetryDelays = [TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2)];
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);
    private static readonly string LibraryVersion = ReadLibraryVersion();

    private readonly PostHogClientOptions _options;
    private readonly HttpClient _http;
    private readonly Uri _batchUrl;
    private readonly Channel<byte[]> _queue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(MaxQueued) { FullMode = BoundedChannelFullMode.Wait });
    private readonly SemaphoreSlim _sending = new SemaphoreSlim(1, 1);
    private readonly CancellationTokenSource _stopping = new CancellationTokenSource();
    private readonly CancellationTokenSource _abandoning = new CancellationTokenSource();
    private readonly Task _sendingPeriodically;
    private int _dropped;
    private int _disposed;

    /// <summary>Creates a client that sends over a connection pool of its own.</summary>
    /// <param name="options">Where to send, and how often.</param>
    public PostHogClient(PostHogClientOptions options)
        : this(options, new SocketsHttpHandler { PooledConnectionLifetime = TimeSpan.FromMinutes(5) }, disposeHandler: true)
    {
    }

    /// <summary>Creates a client that sends through the given handler, which the caller keeps and disposes.</summary>
    /// <param name="options">Where to send, and how often.</param>
    /// <param name="handler">The HTTP handler requests go through, such as a proxy's.</param>
    public PostHogClient(PostHogClientOptions options, HttpMessageHandler handler)
        : this(options, handler, disposeHandler: false)
    {
    }

    private PostHogClient(PostHogClientOptions options, HttpMessageHandler handler, bool disposeHandler)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(handler);
        _options = options;
        _http = new HttpClient(handler, disposeHandler) { Timeout = RequestTimeout };
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue(LibraryName, LibraryVersion));
        _batchUrl = new Uri(options.Host, "batch/");
        _sendingPeriodically = Task.Run(SendPeriodicallyAsync);
    }

    /// <summary>Captures an event.</summary>
    /// <param name="event">What happened, to whom.</param>
    /// <returns>False when the event was dropped: the queue was full, or the client is disposed.</returns>
    public bool Capture(PostHogEvent @event)
    {
        ArgumentNullException.ThrowIfNull(@event);
        return Enqueue(Serialize(@event.Name, @event.DistinctId, @event.Properties, @event.Groups, @event.Timestamp ?? _options.TimeProvider.GetUtcNow()));
    }

    /// <summary>
    /// Captures an exception for error tracking, as a <c>$exception</c> event: its type, message, and
    /// stack, and those of its inner exceptions, which PostHog groups into issues.
    /// </summary>
    /// <param name="exception">What was thrown.</param>
    /// <param name="distinctId">Whom it happened to; for an exception that belongs to no person, any stable ID, with <c>$process_person_profile</c> false in <paramref name="properties"/>.</param>
    /// <param name="properties">More properties, copied.</param>
    /// <returns>False when the event was dropped.</returns>
    public bool CaptureException(Exception exception, string distinctId, JsonObject? properties = null)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ArgumentException.ThrowIfNullOrWhiteSpace(distinctId);
        JsonObject all = properties?.DeepClone().AsObject() ?? new JsonObject();
        all["$exception_list"] = ExceptionList.From(exception);
        all["$exception_level"] = "error";
        all["$exception_type"] = exception.GetType().FullName ?? exception.GetType().Name;
        all["$exception_message"] = exception.Message;
        return Enqueue(Serialize("$exception", distinctId, all, groups: null, _options.TimeProvider.GetUtcNow()));
    }

    /// <summary>Sets properties of a person, as a <c>$identify</c> event.</summary>
    /// <param name="distinctId">The person.</param>
    /// <param name="set">The properties to set, copied.</param>
    /// <returns>False when the event was dropped.</returns>
    public bool Identify(string distinctId, JsonObject set)
    {
        ArgumentNullException.ThrowIfNull(set);
        JsonObject properties = new JsonObject { ["$set"] = set.DeepClone() };
        return Enqueue(Serialize("$identify", distinctId, properties, groups: null, _options.TimeProvider.GetUtcNow()));
    }

    /// <summary>Sets properties of a group, creating it if it's new, as a <c>$groupidentify</c> event.</summary>
    /// <param name="groupType">The group's type, such as <c>workspace</c>.</param>
    /// <param name="groupKey">The group's key, such as its ID.</param>
    /// <param name="set">The properties to set, copied.</param>
    /// <returns>False when the event was dropped.</returns>
    public bool GroupIdentify(string groupType, string groupKey, JsonObject set)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(groupType);
        ArgumentException.ThrowIfNullOrWhiteSpace(groupKey);
        ArgumentNullException.ThrowIfNull(set);
        JsonObject properties = new JsonObject
        {
            ["$group_type"] = groupType,
            ["$group_key"] = groupKey,
            ["$group_set"] = set.DeepClone(),
        };
        return Enqueue(Serialize("$groupidentify", "$" + groupType + "_" + groupKey, properties, groups: null, _options.TimeProvider.GetUtcNow()));
    }

    /// <summary>Sends every queued event now, waiting for PostHog's answers and retries.</summary>
    /// <param name="cancellationToken">Stops waiting; events not yet sent are reported lost.</param>
    public Task FlushAsync(CancellationToken cancellationToken)
    {
        return SendQueuedAsync(cancellationToken);
    }

    /// <summary>Sends what is still queued, for up to <see cref="PostHogClientOptions.ShutdownTimeout"/>, then stops; events not sent by then are reported lost.</summary>
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        _queue.Writer.TryComplete();
        _abandoning.CancelAfter(_options.ShutdownTimeout);
        await _stopping.CancelAsync().ConfigureAwait(false);
        await _sendingPeriodically.ConfigureAwait(false);
        await SendQueuedAsync(_abandoning.Token).ConfigureAwait(false);
        _http.Dispose();
        _sending.Dispose();
        _stopping.Dispose();
        _abandoning.Dispose();
    }

    private bool Enqueue(byte[] serialized)
    {
        if (_queue.Writer.TryWrite(serialized))
        {
            return true;
        }

        if (Volatile.Read(ref _disposed) == 0)
        {
            Interlocked.Increment(ref _dropped);
        }

        return false;
    }

    private async Task SendPeriodicallyAsync()
    {
        using PeriodicTimer timer = new PeriodicTimer(_options.FlushInterval, _options.TimeProvider);
        try
        {
            while (await timer.WaitForNextTickAsync(_stopping.Token).ConfigureAwait(false))
            {
                await SendQueuedAsync(_abandoning.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (_stopping.IsCancellationRequested)
        {
        }
    }

    private async Task SendQueuedAsync(CancellationToken ct)
    {
        try
        {
            await _sending.WaitAsync(ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ReportQueuedIfDisposing();
            return;
        }

        List<byte[]> batch = new List<byte[]>(Math.Min(_queue.Reader.Count, MaxBatch));
        try
        {
            int dropped = Interlocked.Exchange(ref _dropped, 0);
            if (dropped > 0)
            {
                Report(dropped, "queue_full", statusCode: null, exception: null);
            }

            while (_queue.Reader.TryRead(out byte[]? queued))
            {
                batch.Add(queued);
                if (batch.Count == MaxBatch)
                {
                    await SendBatchAsync(batch, ct).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            if (batch.Count > 0)
            {
                await SendBatchAsync(batch, ct).ConfigureAwait(false);
                batch.Clear();
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // The batch on its way is lost. What is still queued goes with the next flush, unless the
            // client is going away.
            Report(batch.Count, "unreachable", statusCode: null, exception: null);
            ReportQueuedIfDisposing();
        }
        finally
        {
            _sending.Release();
        }
    }

    private void ReportQueuedIfDisposing()
    {
        if (Volatile.Read(ref _disposed) == 1)
        {
            Report(_queue.Reader.Count, "unreachable", statusCode: null, exception: null);
        }
    }

    // Sends one batch, again after a failure that may pass, such as 503 or no connection; lost events
    // are reported here, except when the caller gives up, which cancels.
    private async Task SendBatchAsync(List<byte[]> events, CancellationToken ct)
    {
        byte[] body = Compress(events);
        for (int attempt = 0; ; attempt++)
        {
            int? status = null;
            Exception? failure = null;
            try
            {
                using ByteArrayContent content = new ByteArrayContent(body);
                content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
                content.Headers.ContentEncoding.Add("gzip");
                using HttpResponseMessage response = await _http.PostAsync(_batchUrl, content, ct).ConfigureAwait(false);
                if (response.IsSuccessStatusCode)
                {
                    return;
                }

                status = (int)response.StatusCode;
                bool mayPass = response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.RequestTimeout || status >= 500;
                if (!mayPass)
                {
                    Report(events.Count, "refused", status, exception: null);
                    return;
                }
            }
            catch (HttpRequestException exception)
            {
                failure = exception;
            }
            catch (TaskCanceledException exception) when (!ct.IsCancellationRequested)
            {
                failure = exception;
            }

            if (attempt == RetryDelays.Length)
            {
                Report(events.Count, "unreachable", status, failure);
                return;
            }

            await Task.Delay(RetryDelays[attempt], _options.TimeProvider, ct).ConfigureAwait(false);
        }
    }

    private byte[] Compress(List<byte[]> events)
    {
        using MemoryStream compressed = new MemoryStream();
        using (GZipStream gzip = new GZipStream(compressed, CompressionLevel.Fastest, leaveOpen: true))
        using (Utf8JsonWriter writer = new Utf8JsonWriter(gzip))
        {
            writer.WriteStartObject();
            writer.WriteString("api_key", _options.ProjectToken);
            writer.WriteBoolean("historical_migration", false);
            writer.WriteStartArray("batch");
            foreach (byte[] queued in events)
            {
                writer.WriteRawValue(queued, skipInputValidation: true);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return compressed.ToArray();
    }

    private void Report(int events, string reason, int? statusCode, Exception? exception)
    {
        if (events > 0)
        {
            _options.DeliveryFailed?.Invoke(new PostHogDeliveryFailure(events, reason, statusCode, exception));
        }
    }

    // An event as PostHog's batch endpoint takes it, written at capture so the caller's properties
    // can change afterwards. The server's address says nothing about where people are, so GeoIP is
    // off unless the caller turns it on.
    private static byte[] Serialize(string name, string distinctId, JsonObject properties, IDictionary<string, string>? groups, DateTimeOffset timestamp)
    {
        ArrayBufferWriter<byte> buffer = new ArrayBufferWriter<byte>();
        using (Utf8JsonWriter writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("uuid", Guid.CreateVersion7());
            writer.WriteString("event", name);
            writer.WriteString("distinct_id", distinctId);
            writer.WriteString("timestamp", timestamp.ToUniversalTime());
            writer.WriteStartObject("properties");
            foreach (KeyValuePair<string, JsonNode?> property in properties)
            {
                writer.WritePropertyName(property.Key);
                if (property.Value is null)
                {
                    writer.WriteNullValue();
                }
                else
                {
                    property.Value.WriteTo(writer);
                }
            }

            if (groups is { Count: > 0 })
            {
                writer.WriteStartObject("$groups");
                foreach (KeyValuePair<string, string> group in groups)
                {
                    writer.WriteString(group.Key, group.Value);
                }

                writer.WriteEndObject();
            }

            if (!properties.ContainsKey("$lib"))
            {
                writer.WriteString("$lib", LibraryName);
                writer.WriteString("$lib_version", LibraryVersion);
            }

            if (!properties.ContainsKey("$geoip_disable"))
            {
                writer.WriteBoolean("$geoip_disable", true);
            }

            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }

    // The package's version, without the commit SourceLink appends after a plus.
    private static string ReadLibraryVersion()
    {
        string? version = typeof(PostHogClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (version is null)
        {
            return "0";
        }

        int plus = version.IndexOf('+', StringComparison.Ordinal);
        return plus < 0 ? version : version[..plus];
    }
}
