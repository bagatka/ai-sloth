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
    internal sealed record StartChatRequest(string Provider, string Harness, AgentAccountId Account, string Model, string Thinking, IReadOnlyList<NookRepository>? Repositories = null, ChatId? CopyOf = null, int? Checkpoint = null, bool? WithConversation = null);

    internal sealed record AddPresetRequest(string Harness, AgentAccountId Account, string Model, string Thinking, string? Name = null);

    internal sealed record PresetNameRequest(string Name);

    internal sealed record ChangeModelRequest(AgentAccountId Account, string Model, string Thinking);

    internal sealed record PushRequest(IReadOnlyList<string>? Sources = null, string? Branch = null, bool PullRequest = false, string? Message = null);

    internal sealed record SendMessageRequest(string Text, MessageId? Proposal = null);


    internal sealed record InstructionsRequest(string Text);


    // A chat event in a server-sent event: its type is the event's kind, which names the case of Event,
    // and its ID the sequence number.
    internal sealed record ChatEventData(long Sequence, DateTimeOffset At, ChatEventBody Event);

    public static RouteGroupBuilder MapChatsEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/harnesses", ListHarnesses).WithTags("Chats");

        RouteGroupBuilder workspaceChats = app.MapGroup("/workspaces/{workspaceId:guid}/chats").WithTags("Chats");
        workspaceChats.MapPost("/", Start).RequireRateLimiting(RateLimits.Starts).ProducesProblem(StatusCodes.Status429TooManyRequests);
        workspaceChats.MapGet("/", List);
        workspaceChats.MapGet("/live", WatchList);

        RouteGroupBuilder chats = app.MapGroup("/chats").WithTags("Chats");
        chats.MapGet("/{id:guid}", Get);
        chats.MapGet("/{id:guid}/usage", GetUsage);
        chats.MapPost("/{id:guid}/messages", Send);
        chats.MapPost("/{id:guid}/stop", Stop);
        chats.MapPost("/{id:guid}/setup", RunSetupAgain);
        chats.MapPost("/{id:guid}/setup/fix", FixSetup);
        chats.MapPost("/{id:guid}/setup/use-fix", UseSetupFix);
        chats.MapPut("/{id:guid}/model", ChangeModel);
        chats.MapGet("/{id:guid}/events", Watch);
        chats.MapPost("/{id:guid}/push", Push);

        RouteGroupBuilder workspaces = app.MapGroup("/workspaces/{workspaceId:guid}").WithTags("Chats");
        workspaces.MapGet("/harness-state", ListHarnessStates);
        workspaces.MapDelete("/harness-state/{harness}", ForgetHarnessState);
        workspaces.MapGet("/instructions", GetInstructions);
        workspaces.MapPut("/instructions", SetWorkspaceInstructions);
        app.MapPut("/instructions", SetPersonalInstructions).WithTags("Chats");
        workspaces.MapGet("/models", ListModels);
        workspaces.MapGet("/usage", GetWorkspaceUsage);
        workspaces.MapGet("/presets", ListPresets);
        workspaces.MapPost("/presets", AddPreset);

        RouteGroupBuilder presets = app.MapGroup("/presets").WithTags("Chats");
        presets.MapPut("/{id:guid}/name", RenamePreset);
        presets.MapDelete("/{id:guid}", RemovePreset);
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
    /// account (the workspace's, or the caller's own), and with a model from
    /// <c>GET /workspaces/{id}/models</c> or the harness's default. One chat per nook, so agents never
    /// work on each other's files. The agent starts with the first message; others join through the
    /// nook's access.
    /// </summary>
    private static async Task<Results<Created<ChatSummary>, ProblemHttpResult>> Start(
        [FromRoute] Guid workspaceId,
        [FromBody] StartChatRequest request,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        StartChat command = new StartChat(WorkspaceId.From(workspaceId), request.Provider, request.Harness, request.Account, request.Model, request.Thinking, request.Repositories ?? [], request.CopyOf, request.Checkpoint, request.WithConversation == true);
        Result<ChatSummary> result = await api.StartAsync(principal.ToActor(), command, ct);
        return result.ToCreated(chat => string.Create(CultureInfo.InvariantCulture, $"/chats/{chat.Id.Value}"));
    }

    /// <summary>
    /// The models a chat's agent can run on with the harness and the agent account, for <c>model</c>
    /// when starting a chat: those of the account's vendor that can call tools, the ones the harness is
    /// made for first, then in the vendor's order. A new chat starts on the first unless someone
    /// chooses another.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<HarnessModel>>, ProblemHttpResult>> ListModels(
        [FromRoute] Guid workspaceId,
        [FromQuery] string harness,
        [FromQuery] Guid account,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<HarnessModel>> result = await api.ListModelsAsync(principal.ToActor(), WorkspaceId.From(workspaceId), harness, AgentAccountId.From(account), ct);
        return result.ToOk();
    }

    /// <summary>
    /// Your presets that start chats in the workspace, oldest first: on its accounts, or your own.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<PresetSummary>>, ProblemHttpResult>> ListPresets(
        [FromRoute] Guid workspaceId,
        ClaimsPrincipal principal,
        [FromServices] IChatSettingsApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<PresetSummary>> result = await api.ListPresetsAsync(principal.ToActor(), WorkspaceId.From(workspaceId), ct);
        return result.ToOk();
    }

    /// <summary>
    /// Saves a preset of yours: a harness, an agent account of the workspace's or your own that it
    /// takes, and a model for a harness that takes one. Without a name, it is named after its harness
    /// and model. At most 20, each harness, account, and model once.
    /// </summary>
    private static async Task<Results<Created<PresetSummary>, ProblemHttpResult>> AddPreset(
        [FromRoute] Guid workspaceId,
        [FromBody] AddPresetRequest request,
        ClaimsPrincipal principal,
        [FromServices] IChatSettingsApi api,
        CancellationToken ct)
    {
        AddPreset command = new AddPreset(WorkspaceId.From(workspaceId), request.Harness, request.Account, request.Model, request.Thinking, request.Name);
        Result<PresetSummary> result = await api.AddPresetAsync(principal.ToActor(), command, ct);
        return result.ToCreated(preset => string.Create(CultureInfo.InvariantCulture, $"/presets/{preset.Id.Value}"));
    }

    /// <summary>Renames a preset of yours.</summary>
    private static async Task<Results<Ok<PresetSummary>, ProblemHttpResult>> RenamePreset(
        [FromRoute] Guid id,
        [FromBody] PresetNameRequest request,
        ClaimsPrincipal principal,
        [FromServices] IChatSettingsApi api,
        CancellationToken ct)
    {
        Result<PresetSummary> result = await api.RenamePresetAsync(principal.ToActor(), PresetId.From(id), request.Name, ct);
        return result.ToOk();
    }

    /// <summary>Removes a preset of yours. Chats started from it keep running as they are.</summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> RemovePreset(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IChatSettingsApi api,
        CancellationToken ct)
    {
        Result result = await api.RemovePresetAsync(principal.ToActor(), PresetId.From(id), ct);
        return result.ToNoContent();
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

    /// <summary>
    /// The workspace's chats as lists show them, live, as server-sent events of type <c>chats</c>, each
    /// a list of chats: first the newest 100 someone wrote in, newest first, then, each time some of
    /// those change, the ones that changed, such as a chat someone first wrote in or one whose agent
    /// started or stopped working. Each chat replaces what a client had for it. The stream ends when
    /// the host hands over, as in a deploy: connect again, which starts with the newest chats again.
    /// </summary>
    private static async Task<Results<ServerSentEventsResult<IReadOnlyList<ChatSummary>>, ProblemHttpResult>> WatchList(
        [FromRoute] Guid workspaceId,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result<IAsyncEnumerable<IReadOnlyList<ChatSummary>>> result = await api.WatchChatsAsync(principal.ToActor(), WorkspaceId.From(workspaceId), ct);
        if (result.Failed)
        {
            return result.Error.ToProblem();
        }

        return TypedResults.ServerSentEvents(result.Output, "chats");
    }

    /// <summary>
    /// Runs the chat's agent on another model or thinking level, or on another account of the same owner's (the
    /// workspace's, or the chat's owner's own), from its next turn: an agent working on a turn finishes
    /// it, and a new one continues the conversation. Without a model, the harness picks its default.
    /// </summary>
    private static async Task<Results<Ok<ChatSummary>, ProblemHttpResult>> ChangeModel(
        [FromRoute] Guid id,
        [FromBody] ChangeModelRequest request,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result<ChatSummary> result = await api.ChangeModelAsync(principal.ToActor(), new ChangeModel(ChatId.From(id), request.Account, request.Model, request.Thinking), ct);
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
    /// What the workspace's chats used of their models over its last days, today included (1 to 90):
    /// each day's calls, tokens, and cost in the time zone given (IANA, such as <c>Europe/Berlin</c>),
    /// each chat's, the costliest first, and all of them added up. Asks no vendor.
    /// </summary>
    private static async Task<Results<Ok<WorkspaceUsage>, ProblemHttpResult>> GetWorkspaceUsage(
        [FromRoute] Guid workspaceId,
        [FromQuery] int days,
        [FromQuery] string timeZone,
        ClaimsPrincipal principal,
        [FromServices] IChatUsageApi api,
        CancellationToken ct)
    {
        Result<WorkspaceUsage> result = await api.GetWorkspaceUsageAsync(principal.ToActor(), WorkspaceId.From(workspaceId), days, timeZone, ct);
        return result.ToOk();
    }

    /// <summary>
    /// What the chat's agents have used of their models: calls, tokens, and cost, in all and by turn;
    /// how full the context is; and until when the prompt's cache stays. Asks no vendor.
    /// </summary>
    private static async Task<Results<Ok<ChatUsage>, ProblemHttpResult>> GetUsage(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IChatUsageApi api,
        CancellationToken ct)
    {
        Result<ChatUsage> result = await api.GetUsageAsync(principal.ToActor(), ChatId.From(id), ct);
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


    /// <summary>
    /// Your harness state in the workspace, for each harness that keeps any: what its agents write
    /// for themselves to use later, such as Claude Code's memory, saved from your chats there after
    /// each turn that changed it, and given to their next agents there.
    /// </summary>
    private static async Task<Results<Ok<IReadOnlyList<HarnessStateSummary>>, ProblemHttpResult>> ListHarnessStates(
        [FromRoute] Guid workspaceId,
        ClaimsPrincipal principal,
        [FromServices] IChatSettingsApi api,
        CancellationToken ct)
    {
        Result<IReadOnlyList<HarnessStateSummary>> result = await api.ListHarnessStatesAsync(principal.ToActor(), WorkspaceId.From(workspaceId), ct);
        return result.ToOk();
    }

    /// <summary>
    /// Forgets your state for the harness in the workspace, or with <c>shared</c> the workspace's: the
    /// next agents of the chats that would get it start without it.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> ForgetHarnessState(
        [FromRoute] Guid workspaceId,
        [FromRoute] string harness,
        [FromQuery] bool? shared,
        ClaimsPrincipal principal,
        [FromServices] IChatSettingsApi api,
        CancellationToken ct)
    {
        Result result = await api.ForgetHarnessStateAsync(principal.ToActor(), new ForgetHarnessState(WorkspaceId.From(workspaceId), harness, shared ?? false), ct);
        return result.ToNoContent();
    }

    /// <summary>
    /// The instructions your agents in the workspace get, whatever their harness: the workspace's,
    /// and your own.
    /// </summary>
    private static async Task<Results<Ok<Instructions>, ProblemHttpResult>> GetInstructions(
        [FromRoute] Guid workspaceId,
        ClaimsPrincipal principal,
        [FromServices] IChatSettingsApi api,
        CancellationToken ct)
    {
        Result<Instructions> result = await api.GetInstructionsAsync(principal.ToActor(), WorkspaceId.From(workspaceId), ct);
        return result.ToOk();
    }

    /// <summary>
    /// Sets the workspace's instructions for every agent of its chats, up to 10,000 characters of
    /// Markdown; empty for none. Agents that start afterwards follow them.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> SetWorkspaceInstructions(
        [FromRoute] Guid workspaceId,
        [FromBody] InstructionsRequest request,
        ClaimsPrincipal principal,
        [FromServices] IChatSettingsApi api,
        CancellationToken ct)
    {
        Result result = await api.SetWorkspaceInstructionsAsync(principal.ToActor(), WorkspaceId.From(workspaceId), request.Text, ct);
        return result.ToNoContent();
    }

    /// <summary>
    /// Sets your own instructions for every agent of the chats you start, up to 10,000 characters of
    /// Markdown; empty for none. Agents that start afterwards follow them.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> SetPersonalInstructions(
        [FromBody] InstructionsRequest request,
        ClaimsPrincipal principal,
        [FromServices] IChatSettingsApi api,
        CancellationToken ct)
    {
        Result result = await api.SetPersonalInstructionsAsync(principal.ToActor(), request.Text, ct);
        return result.ToNoContent();
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
    /// Runs the chat's nook's setup again, as its scripts are now, such as after its agent fixed them:
    /// once the agent answered every message sent before, it stops, the setup runs, and an agent starts
    /// again with what the setup installed, continuing the conversation. The chat's events tell each
    /// step. People with Write only.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> RunSetupAgain(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result result = await api.RunSetupAgainAsync(principal.ToActor(), ChatId.From(id), ct);
        return result.ToNoContent();
    }

    /// <summary>
    /// Starts a chat that fixes the chat's failed setup, leaving the chat as it is: the same
    /// repositories and agent in a new nook, whose agent is asked, as you, to fix the setup and commit
    /// the fix, after which its setup runs again. Both chats' events tell it. The fix reaches new
    /// nooks once it's pushed and merged. Returns the new chat. People with Write only.
    /// </summary>
    private static async Task<Results<Ok<ChatSummary>, ProblemHttpResult>> FixSetup(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result<ChatSummary> result = await api.FixSetupAsync(principal.ToActor(), ChatId.From(id), ct);
        return result.ToOk();
    }

    /// <summary>
    /// Brings the fix of the chat's failed setup, from the chat that fixed it, into the chat's nook,
    /// then runs its setup again once the agent answered what was sent before: each repository's
    /// changes there are applied here, all of them or none, keeping the chat's own changes. People with
    /// Write only.
    /// </summary>
    private static async Task<Results<NoContent, ProblemHttpResult>> UseSetupFix(
        [FromRoute] Guid id,
        ClaimsPrincipal principal,
        [FromServices] IChatsApi api,
        CancellationToken ct)
    {
        Result result = await api.UseSetupFixAsync(principal.ToActor(), ChatId.From(id), ct);
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
        [FromServices] INookFilesApi nookFiles,
        [FromServices] ISourcesApi sources,
        [FromServices] IWorkspacesApi workspaces,
        [FromServices] IProductEvents productEvents,
        CancellationToken ct)
    {
        Result<IReadOnlyList<ChatPush.PushedSource>> result = await ChatPush.PushAsync(
            principal.ToActor(), ChatId.From(id), request.Sources, request.Branch, request.PullRequest, request.Message, chats, nooks, nookFiles, sources, workspaces, productEvents, ct);
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
            yield return new SseItem<ChatEventData>(new ChatEventData(chatEvent.Sequence, chatEvent.At, chatEvent.Body), chatEvent.Kind)
            {
                EventId = chatEvent.Sequence.ToString(CultureInfo.InvariantCulture),
            };
        }
    }
}
