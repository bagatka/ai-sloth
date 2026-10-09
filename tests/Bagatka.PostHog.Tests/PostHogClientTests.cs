using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Bagatka.PostHog.Tests;

/// <summary>
/// The client against PostHog's batch endpoint: what it sends, how it bounds what waits, when it sends
/// again, and how it describes exceptions for error tracking.
/// </summary>
public sealed class PostHogClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Captured_events_go_together_gzipped_in_PostHogs_batch_shape()
    {
        using FakePostHog postHog = new FakePostHog();
        await using PostHogClient client = Client(postHog);
        PostHogEvent started = new PostHogEvent("chat_started", "person-1")
        {
            Properties = { ["harness"] = "codex", ["seconds"] = 1.5, ["from_ready_copy"] = true },
            Groups = { ["workspace"] = "workspace-1" },
        };

        Assert.True(client.Capture(started));
        Assert.True(client.Identify("person-1", new JsonObject { ["kind"] = "test" }));
        await client.FlushAsync(Ct);

        FakePostHog.Received request = Assert.Single(postHog.Requests);
        Assert.Equal(new Uri(FakePostHog.Host, "batch/"), request.Uri);
        Assert.Equal("gzip", request.ContentEncoding);
        Assert.Equal("phc_test", request.Body.GetProperty("api_key").GetString());
        Assert.False(request.Body.GetProperty("historical_migration").GetBoolean());
        JsonElement[] events = [.. postHog.Events];
        Assert.Equal(2, events.Length);
        Assert.NotEqual(events[0].GetProperty("uuid").GetGuid(), events[1].GetProperty("uuid").GetGuid());
        Assert.Equal("chat_started", events[0].GetProperty("event").GetString());
        Assert.Equal("person-1", events[0].GetProperty("distinct_id").GetString());
        Assert.True(events[0].GetProperty("timestamp").GetDateTimeOffset() > DateTimeOffset.UnixEpoch);
        JsonElement properties = events[0].GetProperty("properties");
        Assert.Equal("codex", properties.GetProperty("harness").GetString());
        Assert.Equal(1.5, properties.GetProperty("seconds").GetDouble());
        Assert.Equal("workspace-1", properties.GetProperty("$groups").GetProperty("workspace").GetString());
        Assert.Equal("bagatka-posthog", properties.GetProperty("$lib").GetString());
        Assert.True(properties.GetProperty("$geoip_disable").GetBoolean());
        Assert.Equal("$identify", events[1].GetProperty("event").GetString());
        Assert.Equal("test", events[1].GetProperty("properties").GetProperty("$set").GetProperty("kind").GetString());
    }

    [Fact]
    public async Task A_full_queue_drops_what_comes_next_and_reports_it()
    {
        using FakePostHog postHog = new FakePostHog();
        ConcurrentQueue<PostHogDeliveryFailure> failures = new ConcurrentQueue<PostHogDeliveryFailure>();
        await using PostHogClient client = Client(postHog, failures);

        bool[] captured = [.. Enumerable.Range(0, 10_001).Select(i => client.Capture(new PostHogEvent("tick", "person-1")))];
        await client.FlushAsync(Ct);

        Assert.Equal(10_000, captured.Count(accepted => accepted));
        Assert.False(captured[^1]);
        Assert.Equal(10, postHog.Requests.Count);
        Assert.Equal(10_000, postHog.Events.Count());
        PostHogDeliveryFailure failure = Assert.Single(failures);
        Assert.Equal(new PostHogDeliveryFailure(1, "queue_full", null, null), failure);
    }

    [Fact]
    public async Task A_failure_that_may_pass_is_sent_again_and_one_that_wont_is_reported()
    {
        using FakePostHog postHog = new FakePostHog();
        ConcurrentQueue<PostHogDeliveryFailure> failures = new ConcurrentQueue<PostHogDeliveryFailure>();
        await using PostHogClient client = Client(postHog, failures);
        postHog.Then(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK, HttpStatusCode.Unauthorized);

        client.Capture(new PostHogEvent("first", "person-1"));
        await client.FlushAsync(Ct);
        client.Capture(new PostHogEvent("second", "person-1"));
        await client.FlushAsync(Ct);

        FakePostHog.Received[] requests = [.. postHog.Requests];
        Assert.Equal(3, requests.Length);
        Assert.Equal(Uuid(requests[0]), Uuid(requests[1]));
        PostHogDeliveryFailure failure = Assert.Single(failures);
        Assert.Equal(new PostHogDeliveryFailure(1, "refused", 401, null), failure);
    }

    [Fact]
    public async Task An_exception_is_described_with_its_inner_ones_and_the_methods_it_passed_through_generic_and_async_ones_too()
    {
        using FakePostHog postHog = new FakePostHog();
        await using PostHogClient client = Client(postHog);
        Exception thrown = await CatchAsync();

        Assert.True(client.CaptureException(thrown, "webapi", new JsonObject { ["$process_person_profile"] = false }));
        await client.FlushAsync(Ct);

        JsonElement properties = Assert.Single(postHog.Events).GetProperty("properties");
        Assert.Equal("$exception", Assert.Single(postHog.Events).GetProperty("event").GetString());
        Assert.False(properties.GetProperty("$process_person_profile").GetBoolean());
        Assert.Equal("error", properties.GetProperty("$exception_level").GetString());
        JsonElement[] exceptions = [.. properties.GetProperty("$exception_list").EnumerateArray()];
        Assert.Equal(2, exceptions.Length);
        Assert.Equal("System.InvalidOperationException", exceptions[0].GetProperty("type").GetString());
        Assert.Equal("Sending failed.", exceptions[0].GetProperty("value").GetString());
        Assert.Equal("System.TimeoutException", exceptions[1].GetProperty("type").GetString());
        JsonElement[] frames = [.. exceptions[1].GetProperty("stacktrace").GetProperty("frames").EnumerateArray()];
        JsonElement thrower = Assert.Single(frames, frame => string.Equals(frame.GetProperty("function").GetString(), nameof(ThrowFromAsyncAsync), StringComparison.Ordinal));
        Assert.Equal(typeof(PostHogClientTests).FullName, thrower.GetProperty("module").GetString());
        Assert.True(thrower.GetProperty("in_app").GetBoolean());
        Assert.DoesNotContain(frames, frame => frame.GetProperty("module").GetString()!.StartsWith("System.Runtime.CompilerServices.", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Disposing_sends_what_is_still_queued()
    {
        using FakePostHog postHog = new FakePostHog();
        PostHogClient client = Client(postHog);
        client.Capture(new PostHogEvent("last", "person-1"));

        await client.DisposeAsync();

        Assert.Equal("last", Assert.Single(postHog.Events).GetProperty("event").GetString());
        Assert.False(client.Capture(new PostHogEvent("too late", "person-1")));
    }

    // The background flush waits an hour, so only the test sends.
    private static PostHogClient Client(FakePostHog postHog, ConcurrentQueue<PostHogDeliveryFailure>? failures = null)
    {
        PostHogClientOptions options = new PostHogClientOptions(FakePostHog.Host, "phc_test")
        {
            FlushInterval = TimeSpan.FromHours(1),
            DeliveryFailed = failures is null ? null : failures.Enqueue,
        };
        return new PostHogClient(options, postHog);
    }

    private static Guid Uuid(FakePostHog.Received request)
    {
        return Assert.Single(request.Body.GetProperty("batch").EnumerateArray()).GetProperty("uuid").GetGuid();
    }

    private static async Task<Exception> CatchAsync()
    {
        try
        {
            await ThrowFromAsyncAsync<string>();
        }
        catch (TimeoutException timeout)
        {
            return new InvalidOperationException("Sending failed.", timeout);
        }

        throw new InvalidOperationException("Nothing was thrown.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task ThrowFromAsyncAsync<T>()
    {
        await Task.Yield();
        throw new TimeoutException("No answer for " + typeof(T).Name + ".");
    }
}
