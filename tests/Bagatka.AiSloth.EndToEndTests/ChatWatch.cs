using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.ServerSentEvents;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A chat's events as a client reads them from its server-sent events, from the first one on. When
/// the stream ends or breaks, as when the control plane's instance is replaced, it resumes after the
/// last event read, as clients do.
/// </summary>
internal sealed class ChatWatch : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan ResumeEvery = TimeSpan.FromSeconds(1);

    private readonly HttpClient _client;
    private readonly ChatSummary _chat;

    // Cancelled when an event takes too long, so the read ends instead of being abandoned mid-way.
    private readonly CancellationTokenSource _patience = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private HttpResponseMessage? _response;
    private Stream? _stream;
    private IAsyncEnumerator<SseItem<string>>? _items;
    private long _after;

    private ChatWatch(HttpClient client, ChatSummary chat)
    {
        _client = client;
        _chat = chat;
    }

    /// <summary>Every event read so far: its type and its <c>event</c> body.</summary>
    public List<(string Type, JsonElement Event)> Seen { get; } = [];

    /// <summary>
    /// What was read so far besides the agent's own updates: an agent still starting when a message
    /// comes can send some, such as its commands, between any two of the chat's events.
    /// </summary>
    public List<(string Type, JsonElement Event)> SeenOfChat => [.. Seen.Where(seen => !string.Equals(seen.Type, "agent-update", StringComparison.Ordinal))];

    /// <summary>How many times the stream ended or broke and was resumed.</summary>
    public int Resumed { get; private set; }

    public static async Task<ChatWatch> OpenAsync(HttpClient client, ChatSummary chat)
    {
        ChatWatch watch = new ChatWatch(client, chat);
        HttpResponseMessage response = await watch.ConnectAsync();
        await Api.ExpectAsync(response, HttpStatusCode.OK);
        await watch.ReadFromAsync(response);
        return watch;
    }

    /// <summary>
    /// Reads until the next event of the type, within a minute or <paramref name="patience"/>, and
    /// returns its body; on timeout, the failure lists the events read before it.
    /// </summary>
    public async Task<JsonElement> NextAsync(string type, TimeSpan? patience = null)
    {
        _patience.CancelAfter(patience ?? Patience);
        while (true)
        {
            SseItem<string> item;
            try
            {
                item = await NextItemAsync();
            }
            catch (OperationCanceledException timedOut) when (_patience.IsCancellationRequested && !TestContext.Current.CancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("No " + type + " event came in time. Read before it: " + string.Join(", ", SeenOfChat.Select(seen => seen.Type)), timedOut);
            }

            JsonElement body = JsonElement.Parse(item.Data).GetProperty("event");
            Seen.Add((item.EventType, body));
            _after = long.Parse(item.EventId!, CultureInfo.InvariantCulture);
            if (string.Equals(item.EventType, type, StringComparison.Ordinal))
            {
                _patience.CancelAfter(Timeout.InfiniteTimeSpan);
                return body;
            }
        }
    }

    public async ValueTask DisposeAsync()
    {
        await CloseAsync();
        _patience.Dispose();
    }

    // The next item, resuming after the last event read whenever the stream ends or breaks.
    private async Task<SseItem<string>> NextItemAsync()
    {
        while (true)
        {
            try
            {
                bool more = await _items!.MoveNextAsync();
                if (more)
                {
                    return _items.Current;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or IOException)
            {
                // Broken off: resumed below.
            }

            await ResumeAsync();
        }
    }

    // Opens the stream again after the last event read, once the host answers.
    private async Task ResumeAsync()
    {
        await CloseAsync();
        Resumed++;
        while (true)
        {
            await Task.Delay(ResumeEvery, _patience.Token);
            try
            {
                HttpResponseMessage response = await ConnectAsync();
                if (response.IsSuccessStatusCode)
                {
                    await ReadFromAsync(response);
                    return;
                }

                response.Dispose();
            }
            catch (HttpRequestException)
            {
                // The host isn't back yet.
            }
        }
    }

    private async Task<HttpResponseMessage> ConnectAsync()
    {
        Uri events = new Uri(string.Create(CultureInfo.InvariantCulture, $"/chats/{_chat.Id.Value}/events?after={_after}"), UriKind.Relative);
        return await _client.GetAsync(events, HttpCompletionOption.ResponseHeadersRead, _patience.Token);
    }

    private async Task ReadFromAsync(HttpResponseMessage response)
    {
        _response = response;
        _stream = await response.Content.ReadAsStreamAsync(_patience.Token);
        _items = SseParser.Create(_stream).EnumerateAsync(_patience.Token).GetAsyncEnumerator(_patience.Token);
    }

    private async Task CloseAsync()
    {
        if (_items is not null)
        {
            await _items.DisposeAsync();
        }

        if (_stream is not null)
        {
            await _stream.DisposeAsync();
        }

        _response?.Dispose();
        (_items, _stream, _response) = (null, null, null);
    }
}
