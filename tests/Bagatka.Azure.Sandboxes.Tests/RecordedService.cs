using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Core;
using Azure.Core.Pipeline;

namespace Bagatka.Azure.Sandboxes.Tests;

/// <summary>
/// The service at the HTTP boundary: answers each request in turn with a response recorded from the
/// real service (Recorded/), and keeps the requests it got.
/// </summary>
internal sealed class RecordedService : HttpMessageHandler
{
    private readonly Queue<(HttpStatusCode Status, string? Recording)> _answers = new Queue<(HttpStatusCode, string?)>();

    public List<(HttpMethod Method, Uri Uri, string? Body, string? Authorization)> Requests { get; } = [];

    public static Uri Endpoint { get; } = SandboxGroupClient.GetEndpoint("eastus2");

    public static SandboxGroupId Group { get; } = new SandboxGroupId("00000000-0000-0000-0000-000000000001", "rg", "group");

    public static string GroupPath => "/subscriptions/00000000-0000-0000-0000-000000000001/resourceGroups/rg/sandboxGroups/group";

    // Answers the next request with a recorded body, or none.
    public RecordedService Then(HttpStatusCode status, string? recording = null)
    {
        _answers.Enqueue((status, recording));
        return this;
    }

    public SandboxGroupClient Client()
    {
        SandboxesClientOptions options = new SandboxesClientOptions { Transport = new HttpClientTransport(new HttpClient(this)) };
        options.Retry.Delay = TimeSpan.FromMilliseconds(1);
        options.Retry.MaxDelay = TimeSpan.FromMilliseconds(1);
        return new SandboxGroupClient(Endpoint, Group, new FixedCredential(), options);
    }

    public static string Read(string recording)
    {
        return File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Recorded", recording + ".json"));
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsStringAsync(cancellationToken);
        }

        Requests.Add((request.Method, request.RequestUri!, body, request.Headers.Authorization?.ToString()));
        (HttpStatusCode status, string? recording) = _answers.Dequeue();
        HttpResponseMessage response = new HttpResponseMessage(status);
        if (recording is not null)
        {
            response.Content = new StringContent(recording is ['{', ..] ? recording : Read(recording), Encoding.UTF8, "application/json");
        }

        return response;
    }

    // Hands out one token for the service's scope, and fails for any other.
    private sealed class FixedCredential : TokenCredential
    {
        public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            if (requestContext.Scopes is not ["https://dynamicsessions.io/.default"])
            {
                throw new InvalidOperationException("Asked for a token for " + string.Join(' ', requestContext.Scopes));
            }

            return new AccessToken("test-token", TimeProvider.System.GetUtcNow().AddHours(1));
        }

        public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(GetToken(requestContext, cancellationToken));
        }
    }
}
