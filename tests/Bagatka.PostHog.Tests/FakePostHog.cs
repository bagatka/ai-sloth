using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Bagatka.PostHog.Tests;

// PostHog's batch endpoint at the HTTP boundary: it keeps every request, unzipped, and answers each
// with the next status the test lined up, then 200.
internal sealed class FakePostHog : HttpMessageHandler
{
    public static readonly Uri Host = new Uri("https://eu.i.posthog.test");

    private readonly ConcurrentQueue<HttpStatusCode> _answers = new ConcurrentQueue<HttpStatusCode>();

    public ConcurrentQueue<Received> Requests { get; } = new ConcurrentQueue<Received>();

    public IEnumerable<JsonElement> Events => Requests.SelectMany(request => request.Body.GetProperty("batch").EnumerateArray());

    public void Then(params HttpStatusCode[] answers)
    {
        foreach (HttpStatusCode answer in answers)
        {
            _answers.Enqueue(answer);
        }
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        await using Stream body = await request.Content!.ReadAsStreamAsync(cancellationToken);
        await using GZipStream unzipped = new GZipStream(body, CompressionMode.Decompress);
        using JsonDocument json = await JsonDocument.ParseAsync(unzipped, cancellationToken: cancellationToken);
        Requests.Enqueue(new Received(request.RequestUri!, string.Join(',', request.Content.Headers.ContentEncoding), json.RootElement.Clone()));
        bool lined = _answers.TryDequeue(out HttpStatusCode next);
        HttpStatusCode answer = lined ? next : HttpStatusCode.OK;
        return new HttpResponseMessage(answer) { Content = new StringContent("{\"status\":\"Ok\"}") };
    }

    public sealed record Received(Uri Uri, string ContentEncoding, JsonElement Body);
}
