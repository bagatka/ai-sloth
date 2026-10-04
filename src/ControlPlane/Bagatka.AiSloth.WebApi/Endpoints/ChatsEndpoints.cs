using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Bagatka.Foundation.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace Bagatka.AiSloth.WebApi.Endpoints;

internal static class ChatsEndpoints
{
    internal sealed record SendMessageRequest(string Text);

    // A chat event in a server-sent event: its type is the event's kind, its ID the sequence number.
    internal sealed record ChatEventData(long Sequence, DateTimeOffset At, object Event);

    public static RouteGroupBuilder MapChatsEndpoints(this IEndpointRouteBuilder app)
    {
        RouteGroupBuilder nookChats = app.MapGroup("/nooks/{nookId:guid}/chats").WithTags("Chats");
        nookChats.MapPost("/", Start);
        nookChats.MapGet("/", List);

        RouteGroupBuilder chats = app.MapGroup("/chats").WithTags("Chats");
        chats.MapGet("/{id:guid}", Get);
        chats.MapPost("/{id:guid}/messages", Send);
        chats.MapPost("/{id:guid}/stop", Stop);
        chats.MapGet("/{id:guid}/events", Watch);
        return chats;
    }

    /// <summary>Starts a chat with a coding agent in the nook; the agent starts with the first message.</summary>
    private static async Task<Results<Created<ChatSummary>, ProblemHttpResult>> Start(
        [FromRoute] Guid nookId,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result<ChatSummary> result = await api.StartAsync(principal.ToActor(), new StartChat(NookId.From(nookId)), ct);
        return result.ToCreated(chat => string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}"));
    }

    /// <summary>The nook's chats, newest first, and whether each agent is working.</summary>
    private static async Task<Results<Ok<Page<ChatSummary>>, ProblemHttpResult>> List(
        [FromRoute] Guid nookId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result<Page<ChatSummary>> result = await api.ListAsync(principal.ToActor(), NookId.From(nookId), Paging.Request(cursor, limit), ct);
        return result.ToOk();
    }

    /// <summary>A chat, and whether its agent is working.</summary>
    private static async Task<Results<Ok<ChatSummary>, ProblemHttpResult>> Get(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result<ChatSummary> result = await api.GetAsync(principal.ToActor(), ChatId.From(id), ct);
        return result.ToOk();
    }

    /// <summary>
    /// Sends a message to the agent. It is never refused because the agent is working: it joins the
    /// running turn when the agent supports that, and otherwise starts the next turn.
    /// </summary>
    private static async Task<Results<Ok<ChatMessage>, ProblemHttpResult>> Send(
        [FromRoute] Guid id,
        [FromBody] SendMessageRequest request,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result<ChatMessage> result = await api.SendAsync(principal.ToActor(), new SendMessage(ChatId.From(id), request.Text), ct);
        return result.ToOk();
    }

    /// <summary>Stops the agent: the running turn ends as cancelled, and messages it hasn't received are cancelled.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> Stop(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result result = await api.StopAsync(principal.ToActor(), ChatId.From(id), ct);
        return result.ToNoContent();
    }

    /// <summary>
    /// The chat's events as server-sent events, after the sequence number in <c>after</c> or the
    /// <c>Last-Event-ID</c> header: first those saved, then live. Event types: <c>message-sent</c>,
    /// <c>turn-started</c>, <c>message-steered</c>, <c>message-cancelled</c>, <c>agent-update</c>
    /// (an Agent Client Protocol session update), and <c>turn-ended</c>.
    /// </summary>
    private static async Task<Results<ServerSentEventsResult<ChatEventData>, ProblemHttpResult>> Watch(
        [FromRoute] Guid id,
        [FromQuery] long? after,
        [FromHeader(Name = "Last-Event-ID")] long? lastEventId,
        ClaimsPrincipal principal,
        HttpResponse response,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        WatchChat command = new WatchChat(ChatId.From(id), after ?? lastEventId ?? 0);
        Result<IAsyncEnumerable<ChatEvent>> result = await api.WatchAsync(principal.ToActor(), command, ct);
        return result.TryGetValue(out IAsyncEnumerable<ChatEvent>? events, out Error? error)
            ? TypedResults.ServerSentEvents(AsServerSentEvents(response, events, ct))
            : error.ToProblem();
    }

    private static async IAsyncEnumerable<SseItem<ChatEventData>> AsServerSentEvents(HttpResponse response, IAsyncEnumerable<ChatEvent> events, [EnumeratorCancellation] CancellationToken ct)
    {
        // The headers go out now, not with the first event, so a client watching an idle chat knows it is connected.
        await response.Body.FlushAsync(ct);
        await foreach (ChatEvent chatEvent in events.WithCancellation(ct))
        {
            (string type, object body) = chatEvent.Body switch
            {
                MessageSent sent => ("message-sent", (object)sent),
                TurnStarted started => ("turn-started", started),
                MessageSteered steered => ("message-steered", steered),
                MessageCancelled cancelled => ("message-cancelled", cancelled),
                AgentUpdate update => ("agent-update", update),
                TurnEnded ended => ("turn-ended", ended),
            };
            yield return new SseItem<ChatEventData>(new ChatEventData(chatEvent.Sequence, chatEvent.At, body), type)
            {
                EventId = chatEvent.Sequence.ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
