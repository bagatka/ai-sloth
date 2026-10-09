# Bagatka.PostHog

A [PostHog](https://posthog.com) client for .NET that works with Native AOT and trimming: product
analytics events, identify and group identify, and exceptions for error tracking. It depends on
nothing beyond .NET.

PostHog's own .NET SDK, `PostHog` 2.15.8, produces trim and AOT warnings (IL2104, IL3053) when
published with Native AOT, so command-line tools and daemons built that way can't use it safely.

```csharp
await using PostHogClient client = new PostHogClient(new PostHogClientOptions(new Uri("https://eu.i.posthog.com"), "phc_..."));

client.Capture(new PostHogEvent("chat_started", personId)
{
    Properties = { ["harness"] = "codex", ["from_ready_copy"] = true },
    Groups = { ["workspace"] = workspaceId },
});
client.GroupIdentify("workspace", workspaceId, new JsonObject { ["members"] = 3 });

try
{
    await SendAsync();
}
catch (HttpRequestException exception)
{
    client.CaptureException(exception, personId);
}
```

- **Covered:** `Capture`, `CaptureException` (error tracking's `$exception` events, with inner
  exceptions and each stack's frames, methods named in a way that works under Native AOT),
  `Identify`, `GroupIdentify`, `FlushAsync`. Not yet: feature flags, aliases, and person
  properties set on capture.
- **Sending:** capturing never blocks. Events wait in a queue of at most 10,000 and go in the
  background every `FlushInterval` (five seconds unless given), up to 1,000 a request, gzipped, to
  the project's `/batch/` endpoint. A request PostHog fails or can't be reached for is sent again
  twice; events carry UUIDs, so PostHog keeps one of each. Disposing sends what is still queued, for
  up to five seconds.
- **Lost events:** dropped when the queue is full, refused by PostHog, or out of reach after
  retries; `DeliveryFailed` is told how many and why.
- **Privacy:** GeoIP is off unless an event turns it on, as a server's address says nothing about
  where people are. The client sends only what it is given.
- **Logs, traces, and metrics** go to PostHog over OpenTelemetry's OTLP exporter, at
  `<host>/i/v1/<signal>` with the project token as a Bearer token; this client isn't needed for them.

## Tests

`tests/Bagatka.PostHog.Tests`, against a fake batch endpoint: what is sent and in what shape, the
queue's bound, retries, the exception format, and sending on dispose.
