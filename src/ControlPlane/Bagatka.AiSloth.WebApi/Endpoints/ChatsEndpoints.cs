using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.AgentAccounts.Contracts;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.AiSloth.Sources.Contracts;
using Bagatka.AiSloth.WebApi.Composition;
using Bagatka.AiSloth.Workspaces.Contracts;
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
    internal sealed record StartChatRequest(string Provider, string Harness, AgentAccountId Account, IReadOnlyList<NookRepository>? Repositories = null, ChatId? CopyOf = null);

    internal sealed record PushRequest(IReadOnlyList<string>? Sources = null, string? Branch = null, bool PullRequest = false, string? Message = null);

    internal sealed record SendMessageRequest(string Text, MessageId? Proposal = null);


    // A chat event in a server-sent event: its type is the event's kind, its ID the sequence number.
    internal sealed record ChatEventData(long Sequence, DateTimeOffset At, object Event);

    public static RouteGroupBuilder MapChatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/harnesses", ListHarnesses).WithTags("Chats");

        RouteGroupBuilder workspaceChats = app.MapGroup("/workspaces/{workspaceId:guid}/chats").WithTags("Chats");
        workspaceChats.MapPost("/", Start);
        workspaceChats.MapGet("/", List);

        RouteGroupBuilder chats = app.MapGroup("/chats").WithTags("Chats");
        chats.MapGet("/{id:guid}", Get);
        chats.MapPost("/{id:guid}/messages", Send);
        chats.MapPost("/{id:guid}/stop", Stop);
        chats.MapGet("/{id:guid}/events", Watch);
        chats.MapPost("/{id:guid}/push", Push);
        return chats;
    }

    /// <summary>The harnesses chats can run, and the kinds of agent account each takes.</summary>
    private static async Task<Ok<IReadOnlyList<HarnessSummary>>> ListHarnesses(
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        IReadOnlyList<HarnessSummary> harnesses = await api.ListHarnessesAsync(principal.ToActor(), ct);
        return TypedResults.Ok(harnesses);
    }

    /// <summary>
    /// Starts a chat with a coding agent, and creates the nook it works in: on a provider from
    /// <c>GET /workspaces/{id}/providers</c>, with a harness from <c>GET /harnesses</c>, on an agent
    /// account (the workspace's, or the caller's own). One chat per nook, so agents never work on each
    /// other's files. The agent starts with the first message; others join through the nook's access.
    /// </summary>
    private static async Task<Results<Created<ChatSummary>, ProblemHttpResult>> Start(
        [FromRoute] Guid workspaceId,
        [FromBody] StartChatRequest request,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        StartChat command = new StartChat(WorkspaceId.From(workspaceId), request.Provider, request.Harness, request.Account, request.Repositories ?? [], request.CopyOf);
        Result<ChatSummary> result = await api.StartAsync(principal.ToActor(), command, ct);
        return result.ToCreated(chat => string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}"));
    }

    /// <summary>The workspace's chats, newest first, and whether each agent is working.</summary>
    private static async Task<Results<Ok<Page<ChatSummary>>, ProblemHttpResult>> List(
        [FromRoute] Guid workspaceId,
        [FromQuery] string? cursor,
        [FromQuery] int? limit,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result<Page<ChatSummary>> result = await api.ListAsync(principal.ToActor(), WorkspaceId.From(workspaceId), Paging.Request(cursor, limit), ct);
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
    /// Sends a message. It reaches the agent when the chat runs on the workspace's account or the
    /// sender's own, and is otherwise a proposal the account's owner may send on, by passing its ID as
    /// <c>proposal</c>. It is never refused because the agent is working: it joins the running turn when
    /// the agent supports that, and otherwise starts the next turn.
    /// </summary>
    private static async Task<Results<Ok<ChatMessage>, ProblemHttpResult>> Send(
        [FromRoute] Guid id,
        [FromBody] SendMessageRequest request,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result<ChatMessage> result = await api.SendAsync(principal.ToActor(), new SendMessage(ChatId.From(id), request.Text, request.Proposal), ct);
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
    /// Pushes the changes in the chat's repositories to GitHub with your connection: what isn't
    /// committed is committed as you, then each repository's commits go to one branch, your prefix and
    /// the chat's short ID unless you name another, and a pull request opens when asked. A branch is
    /// created or moved forward, never the default branch, never forced. Each repository's outcome is
    /// its own: one refused leaves the others pushed. People with Write only.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<ChatPush.PushedSource>>, ProblemHttpResult>> Push(
        [FromRoute] Guid id,
        [FromBody] PushRequest request,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi chats,
        [FromServices] INooksApi nooks,
        [FromServices] ISourcesApi sources,
        [FromServices] IWorkspacesApi workspaces,
        CancellationToken ct)
    {
        Result<IReadOnlyList<ChatPush.PushedSource>> result = await ChatPush.PushAsync(
            principal.ToActor(), ChatId.From(id), request.Sources, request.Branch, request.PullRequest, request.Message, chats, nooks, sources, workspaces, ct);
        return result.ToOk();
    }

    /// <summary>
    /// The chat's events as server-sent events, after the sequence number in <c>after</c> or the
    /// <c>Last-Event-ID</c> header: first those saved, then live. Event types: <c>message-sent</c>,
    /// <c>message-proposed</c>, <c>turn-started</c>, <c>message-steered</c>, <c>message-cancelled</c>, <c>agent-update</c>
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
        if (result.Failed)
        {
            return result.Error.ToProblem();
        }

        return TypedResults.ServerSentEvents(AsServerSentEvents(response, result.Output, ct));
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
                MessageProposed proposed => ("message-proposed", proposed),
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
