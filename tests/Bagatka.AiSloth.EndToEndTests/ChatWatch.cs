using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
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
/// A chat's events as a client reads them from its server-sent events, from the first one on.
/// </summary>
internal sealed class ChatWatch : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly HttpResponseMessage _response;
    private readonly Stream _stream;

    // Cancelled when an event takes too long, so the read ends instead of being abandoned mid-way.
    private readonly CancellationTokenSource _patience = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
    private readonly IAsyncEnumerator<SseItem<string>> _items;

    private ChatWatch(HttpResponseMessage response, Stream stream)
    {
        _response = response;
        _stream = stream;
        _items = SseParser.Create(stream).EnumerateAsync(_patience.Token).GetAsyncEnumerator(_patience.Token);
    }

    /// <summary>Every event read so far: its type and its <c>event</c> body.</summary>
    public List<(string Type, JsonElement Event)> Seen { get; } = [];

    public static async Task<ChatWatch> OpenAsync(HttpClient client, ChatSummary chat)
    {
        Uri events = new Uri(string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}/events"), UriKind.Relative);
        HttpResponseMessage response = await client.GetAsync(events, HttpCompletionOption.ResponseHeadersRead, TestContext.Current.CancellationToken);
        await Api.ExpectAsync(response, HttpStatusCode.OK);
        Stream stream = await response.Content.ReadAsStreamAsync(TestContext.Current.CancellationToken);
        return new ChatWatch(response, stream);
    }

    /// <summary>Reads until the next event of the type, within a minute or <paramref name="patience"/>, and returns its body.</summary>
    public async Task<JsonElement> NextAsync(string type, TimeSpan? patience = null)
    {
        _patience.CancelAfter(patience ?? Patience);
        while (await _items.MoveNextAsync())
        {
            JsonElement body = JsonElement.Parse(_items.Current.Data).GetProperty("event");
            Seen.Add((_items.Current.EventType, body));
            if (string.Equals(_items.Current.EventType, type, StringComparison.Ordinal))
            {
                _patience.CancelAfter(Timeout.InfiniteTimeSpan);
                return body;
            }
        }

        throw new InvalidOperationException("The chat's events ended before a " + type + " event.");
    }

    public async ValueTask DisposeAsync()
    {
        await _items.DisposeAsync();
        await _stream.DisposeAsync();
        _response.Dispose();
        _patience.Dispose();
    }
}
