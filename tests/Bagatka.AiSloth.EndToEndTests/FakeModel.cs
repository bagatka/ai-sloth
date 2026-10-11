using System;
using System.Collections.Concurrent;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A model provider at the HTTP boundary: the real agent runs in a real nook, only the model is fake.
/// It speaks the Anthropic Messages API well enough for Claude Code, answering by the last user
/// message's latest text: a tool result last ends the turn, "write hello" asks to write <c>/work/hello.txt</c>, a
/// request to prepare a chat writes a <c>.agents/setup</c> that installs what was installed by hand
/// (<c>/opt/by-hand</c>), a phrase a test holds on (<see cref="HoldOn"/>) holds the answer until the
/// test releases it, "first message" answers with the conversation's first message, and anything else
/// gets a short text. It speaks
/// OpenAI's Responses API well enough for Codex and pi, answering every call with a short text. It
/// stands in for OpenRouter too: its catalog of models, and OpenAI's chat API, which pi calls there,
/// answering every call with a short text.
/// </summary>
internal sealed class FakeModel : IAsyncDisposable
{
    /// <summary>The key tests give workspaces' Anthropic accounts; the gateway forwards it.</summary>
    public const string ApiKey = "e2e-model-key";

    /// <summary>The key tests give OpenRouter accounts; the gateway forwards it as a bearer token.</summary>
    public const string OpenRouterKey = "sk-or-e2e";

    /// <summary>A model in its OpenRouter catalog that can call tools, as a chat's model.</summary>
    public const string OpenRouterModel = "openai/gpt-6-luna";

    /// <summary>Another model in its OpenRouter catalog that can call tools.</summary>
    public const string OpenRouterClaude = "anthropic/claude-opus-5.5";

    private readonly WebApplication _app;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, Hold> _holds = new ConcurrentDictionary<string, Hold>(StringComparer.OrdinalIgnoreCase);
    private int _cut;

    private FakeModel(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Its Anthropic API, as an account names it: the root, such as <c>http://127.0.0.1:41235/</c>.</summary>
    public Uri Url { get; private set; } = new Uri("http://localhost");

    /// <summary>Its OpenAI API, as an account names it: <c>…/v1</c>.</summary>
    public Uri OpenAIUrl => new Uri(Url, "v1");

    /// <summary>
    /// Holds every call whose last message says <paramref name="phrase"/> until <see cref="Release"/>.
    /// Journeys on one host hold on phrases of their own, none containing another's, so they never
    /// release each other's calls.
    /// </summary>
    public void HoldOn(string phrase)
    {
        _holds.GetOrAdd(phrase, _ => new Hold());
    }

    /// <summary>
    /// Waits, a minute at most, for the next call held on <paramref name="phrase"/>, so a test knows the
    /// agent's turn is running.
    /// </summary>
    public async Task HeldAsync(string phrase)
    {
        using CancellationTokenSource patience = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        patience.CancelAfter(Patience);
        try
        {
            await _holds.GetOrAdd(phrase, _ => new Hold()).Started.Reader.ReadAsync(patience.Token);
        }
        catch (OperationCanceledException timedOut) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException("No call held on \"" + phrase + "\" in time.", timedOut);
        }
    }

    /// <summary>Held calls whose caller gave up before the release, as when something between them stopped.</summary>
    public int Cut => Volatile.Read(ref _cut);

    /// <summary>Every call's body as JSON, both APIs', so tests can see what agents told the model.</summary>
    public ConcurrentQueue<string> Requests { get; } = new ConcurrentQueue<string>();

    /// <summary>The credentials each Anthropic message call carried, as the gateway forwarded them.</summary>
    public ConcurrentQueue<(string? ApiKey, string? Authorization)> Credentials { get; } = new ConcurrentQueue<(string? ApiKey, string? Authorization)>();

    /// <summary>The Authorization header each OpenAI responses call carried, as the gateway forwarded it.</summary>
    public ConcurrentQueue<string?> OpenAIAuthorizations { get; } = new ConcurrentQueue<string?>();

    public static async Task<FakeModel> StartAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        WebApplication app = builder.Build();
        FakeModel model = new FakeModel(app);
        app.MapMethods("/api/hello", ["GET", "HEAD"], () => Results.Ok());
        app.MapPost("/v1/messages/count_tokens", () => Results.Json(new { input_tokens = 10 }));
        app.MapPost("/v1/messages", model.MessagesAsync);
        app.MapPost("/v1/responses", model.ResponsesAsync);
        app.MapPost("/v1/chat/completions", model.ChatCompletionsAsync);
        app.MapGet("/v1/models", OpenRouterCatalog);
        await app.StartAsync();
        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        if (addresses is null)
        {
            throw new InvalidOperationException("Kestrel reported no addresses.");
        }

        model.Url = new Uri(addresses.Addresses.Single());
        return model;
    }

    /// <summary>Answers the calls held on <paramref name="phrase"/> so far; later ones hold until the next release.</summary>
    public void Release(string phrase)
    {
        _holds.GetOrAdd(phrase, _ => new Hold()).Release();
    }

    /// <summary>
    /// Answers the calls held on <paramref name="phrase"/> so far and every later one at once, as a model
    /// does: an agent's own side calls, such as one for a chat's title, carry the message that held its
    /// turn.
    /// </summary>
    public void ReleaseForGood(string phrase)
    {
        _holds.GetOrAdd(phrase, _ => new Hold()).ReleaseForGood();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (Hold hold in _holds.Values)
        {
            hold.ReleaseForGood();
        }

        await _app.DisposeAsync();
    }

    // Holds the call until the hold's next release; one whose caller gives up first counts as cut.
    private async Task HoldAsync(Hold hold, CancellationToken callerLeft)
    {
        Task released = hold.Released;
        hold.Started.Writer.TryWrite(true);
        try
        {
            await released.WaitAsync(callerLeft);
        }
        catch (OperationCanceledException) when (callerLeft.IsCancellationRequested)
        {
            Interlocked.Increment(ref _cut);
            throw;
        }
    }

    private async Task MessagesAsync(HttpContext context)
    {
        Credentials.Enqueue((context.Request.Headers["X-Api-Key"].FirstOrDefault(), context.Request.Headers.Authorization.FirstOrDefault()));
        JsonNode? request = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        if (request is null)
        {
            throw new InvalidOperationException("The call had no body.");
        }

        Requests.Enqueue(request.ToJsonString());

        JsonArray messages = request["messages"]!.AsArray();
        JsonNode? lastUser = messages.LastOrDefault(message => string.Equals((string?)message!["role"], "user", StringComparison.Ordinal));
        bool tools = request["tools"] is JsonArray offered && offered.Any(tool => string.Equals((string?)tool!["name"], "Write", StringComparison.Ordinal));
        bool shell = request["tools"] is JsonArray offers && offers.Any(tool => string.Equals((string?)tool!["name"], "Bash", StringComparison.Ordinal));

        JsonObject[] blocks;
        string stopReason;
        // A tool's result ends the turn, unless a new prompt follows it in the same message.
        if (string.Equals((string?)Blocks(lastUser).LastOrDefault()?["type"], "tool_result", StringComparison.Ordinal))
        {
            (blocks, stopReason) = ([Text("Done.")], "end_turn");
        }
        else if (tools && Says(lastUser, "write hello"))
        {
            JsonObject write = new JsonObject { ["type"] = "tool_use", ["id"] = "toolu_e2e", ["name"] = "Write", ["input"] = new JsonObject { ["file_path"] = "/work/hello.txt", ["content"] = "hi from the fake model\n" } };
            (blocks, stopReason) = ([write], "tool_use");
        }
        else if (shell && SetupCommand(messages, lastUser) is string command)
        {
            (blocks, stopReason) = ([Shell(command)], "tool_use");
        }
        else if (Says(lastUser, "first message"))
        {
            JsonNode? firstUser = messages.FirstOrDefault(message => string.Equals((string?)message!["role"], "user", StringComparison.Ordinal));
            (blocks, stopReason) = ([Text("You first said: " + LatestText(firstUser))], "end_turn");
        }
        else if (_holds.FirstOrDefault(held => Says(lastUser, held.Key)).Value is { Holding: true } hold)
        {
            await HoldAsync(hold, context.RequestAborted);
            (blocks, stopReason) = ([Text("Released.")], "end_turn");
        }
        else
        {
            (blocks, stopReason) = ([Text("Hello.")], "end_turn");
        }

        if (request["stream"]?.GetValue<bool>() == true)
        {
            context.Response.ContentType = "text/event-stream";
            await context.Response.WriteAsync(Stream(blocks, stopReason), context.RequestAborted);
        }
        else
        {
            JsonObject message = Message(stopReason);
            message["content"] = new JsonArray([.. blocks.Select(block => block.DeepClone())]);
            await context.Response.WriteAsJsonAsync(message, context.RequestAborted);
        }
    }

    // A streamed response with one short message, as OpenAI's Responses API streams it. Codex and pi
    // read the item events and end the call at response.completed.
    private async Task ResponsesAsync(HttpContext context)
    {
        OpenAIAuthorizations.Enqueue(context.Request.Headers.Authorization.FirstOrDefault());
        JsonNode? request = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        Requests.Enqueue(request?.ToJsonString() ?? string.Empty);
        string? modelName = (string?)request?["model"];
        JsonObject content = new JsonObject { ["type"] = "output_text", ["text"] = "Hello.", ["annotations"] = new JsonArray() };
        JsonObject item = new JsonObject { ["id"] = "msg_e2e", ["type"] = "message", ["role"] = "assistant", ["status"] = "completed", ["content"] = new JsonArray(content) };
        JsonObject started = new JsonObject { ["id"] = "resp_e2e", ["object"] = "response", ["status"] = "in_progress", ["model"] = modelName, ["output"] = new JsonArray() };
        JsonObject completed = new JsonObject
        {
            ["id"] = "resp_e2e",
            ["object"] = "response",
            ["status"] = "completed",
            ["model"] = modelName,
            ["output"] = new JsonArray(item.DeepClone()),
            ["usage"] = new JsonObject
            {
                ["input_tokens"] = 10,
                ["input_tokens_details"] = new JsonObject { ["cached_tokens"] = 0 },
                ["output_tokens"] = 3,
                ["output_tokens_details"] = new JsonObject { ["reasoning_tokens"] = 0 },
                ["total_tokens"] = 13,
            },
        };
        JsonObject added = (JsonObject)item.DeepClone();
        added["status"] = "in_progress";
        added["content"] = new JsonArray();
        JsonObject[] events =
        [
            new JsonObject { ["type"] = "response.created", ["response"] = started },
            new JsonObject { ["type"] = "response.output_item.added", ["output_index"] = 0, ["item"] = added },
            new JsonObject { ["type"] = "response.content_part.added", ["item_id"] = "msg_e2e", ["output_index"] = 0, ["content_index"] = 0, ["part"] = new JsonObject { ["type"] = "output_text", ["text"] = string.Empty, ["annotations"] = new JsonArray() } },
            new JsonObject { ["type"] = "response.output_text.delta", ["item_id"] = "msg_e2e", ["output_index"] = 0, ["content_index"] = 0, ["delta"] = "Hello." },
            new JsonObject { ["type"] = "response.output_text.done", ["item_id"] = "msg_e2e", ["output_index"] = 0, ["content_index"] = 0, ["text"] = "Hello." },
            new JsonObject { ["type"] = "response.content_part.done", ["item_id"] = "msg_e2e", ["output_index"] = 0, ["content_index"] = 0, ["part"] = content.DeepClone() },
            new JsonObject { ["type"] = "response.output_item.done", ["output_index"] = 0, ["item"] = item },
            new JsonObject { ["type"] = "response.completed", ["response"] = completed },
        ];
        context.Response.ContentType = "text/event-stream";
        await context.Response.WriteAsync(EventStream(events), context.RequestAborted);
    }

    // OpenRouter's catalog, as it lists models: two that can call tools, one only for batches, and one
    // that can't call tools.
    private static IResult OpenRouterCatalog()
    {
        JsonObject catalog = new JsonObject
        {
            ["data"] = new JsonArray(
                CatalogModel(OpenRouterModel, "OpenAI: GPT-6 Luna", "tools", "max_tokens"),
                CatalogModel(OpenRouterModel + ":batch", "OpenAI: GPT-6 Luna (batch)", "tools", "max_tokens"),
                CatalogModel(OpenRouterClaude, "Anthropic: Claude Opus 5.5", "tools", "max_tokens"),
                CatalogModel("example/writer", "Example: Writer", "max_tokens")),
        };
        return Results.Text(catalog.ToJsonString(), "application/json");
    }

    private static JsonObject CatalogModel(string id, string name, params string[] parameters)
    {
        return new JsonObject
        {
            ["id"] = id,
            ["name"] = name,
            ["supported_parameters"] = new JsonArray([.. parameters.Select(parameter => (JsonNode)parameter)]),
        };
    }

    // OpenAI's chat API, as pi calls it at OpenRouter: every call gets a short text, streamed or not.
    private async Task ChatCompletionsAsync(HttpContext context)
    {
        OpenAIAuthorizations.Enqueue(context.Request.Headers.Authorization.FirstOrDefault());
        JsonNode? request = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted);
        Requests.Enqueue(request?.ToJsonString() ?? string.Empty);
        string? modelName = (string?)request?["model"];
        JsonObject usage = new JsonObject { ["prompt_tokens"] = 10, ["completion_tokens"] = 3, ["total_tokens"] = 13 };
        if ((bool?)request?["stream"] != true)
        {
            JsonObject message = new JsonObject { ["role"] = "assistant", ["content"] = "Hello." };
            JsonObject completion = new JsonObject
            {
                ["id"] = "chatcmpl_e2e",
                ["object"] = "chat.completion",
                ["created"] = 0,
                ["model"] = modelName,
                ["choices"] = new JsonArray(new JsonObject { ["index"] = 0, ["message"] = message, ["finish_reason"] = "stop" }),
                ["usage"] = usage,
            };
            await context.Response.WriteAsJsonAsync(completion, context.RequestAborted);
            return;
        }

        JsonObject[] chunks =
        [
            Chunk(modelName, new JsonObject { ["role"] = "assistant", ["content"] = string.Empty }, finish: null, used: null),
            Chunk(modelName, new JsonObject { ["content"] = "Hello." }, finish: null, used: null),
            Chunk(modelName, new JsonObject(), "stop", used: null),
            Chunk(modelName, delta: null, finish: null, usage),
        ];
        StringBuilder stream = new StringBuilder();
        foreach (JsonObject chunk in chunks)
        {
            stream.Append("data: ").Append(chunk.ToJsonString()).Append("\n\n");
        }

        stream.Append("data: [DONE]\n\n");
        context.Response.ContentType = "text/event-stream";
        await context.Response.WriteAsync(stream.ToString(), context.RequestAborted);
    }

    // A piece of a streamed chat answer: some of the message, how it finished, or what it used.
    private static JsonObject Chunk(string? model, JsonObject? delta, string? finish, JsonObject? used)
    {
        return new JsonObject
        {
            ["id"] = "chatcmpl_e2e",
            ["object"] = "chat.completion.chunk",
            ["created"] = 0,
            ["model"] = model,
            ["choices"] = delta is null ? new JsonArray() : new JsonArray(new JsonObject { ["index"] = 0, ["delta"] = delta, ["finish_reason"] = finish }),
            ["usage"] = used,
        };
    }

    private static JsonObject Text(string text)
    {
        return new JsonObject { ["type"] = "text", ["text"] = text };
    }

    // What the agent runs when asked to prepare the project, once in a conversation. Any text of the message counts, because Claude Code sends a
    // later turn's prompt with the earlier turn's tool result and adds reminders.
    private static string? SetupCommand(JsonArray messages, JsonNode? message)
    {
        string text = message?["content"] is JsonValue plain
            ? plain.GetValue<string>()
            : string.Join("\n", Blocks(message).Select(block => block["text"]?.GetValue<string>()));
        (string Asked, string Command)[] steps =
        [
            ("Prepare this chat's files for AiSloth", "mkdir -p /work/.agents && printf '#!/bin/sh\\nmkdir -p /opt && touch /opt/by-hand\\n' > /work/.agents/setup && chmod +x /work/.agents/setup"),
        ];
        return steps
            .Where(step => text.Contains(step.Asked, StringComparison.Ordinal) && !Ran(messages, step.Command))
            .Select(step => step.Command)
            .FirstOrDefault();
    }

    private static bool Ran(JsonArray messages, string command)
    {
        return messages.Where(message => string.Equals((string?)message!["role"], "assistant", StringComparison.Ordinal))
            .SelectMany(Blocks)
            .Any(block => string.Equals((string?)block["type"], "tool_use", StringComparison.Ordinal) && string.Equals((string?)block["input"]?["command"], command, StringComparison.Ordinal));
    }

    private static JsonObject Shell(string command)
    {
        return new JsonObject { ["type"] = "tool_use", ["id"] = "toolu_e2e_" + Guid.CreateVersion7().ToString("N", CultureInfo.InvariantCulture), ["name"] = "Bash", ["input"] = new JsonObject { ["command"] = command, ["description"] = "Write the setup" } };
    }

    private static IEnumerable<JsonObject> Blocks(JsonNode? message)
    {
        return message?["content"] is JsonArray content ? content.OfType<JsonObject>() : [];
    }

    // Only the latest text counts: after a stop, Claude Code sends the stopped prompt and the next one
    // as one user message.
    private static bool Says(JsonNode? message, string words)
    {
        return LatestText(message)?.Contains(words, StringComparison.OrdinalIgnoreCase) == true;
    }

    private static string? LatestText(JsonNode? message)
    {
        return message?["content"] is JsonValue plain
            ? plain.GetValue<string>()
            : Blocks(message).LastOrDefault(block => block["text"] is not null)?["text"]?.GetValue<string>();
    }

    private static JsonObject Message(string? stopReason)
    {
        return new JsonObject
        {
            ["id"] = "msg_e2e",
            ["type"] = "message",
            ["role"] = "assistant",
            ["model"] = "claude-fake",
            ["content"] = new JsonArray(),
            ["stop_reason"] = stopReason,
            ["stop_sequence"] = null,
            ["usage"] = new JsonObject { ["input_tokens"] = 10, ["output_tokens"] = 3 },
        };
    }

    private static string Stream(JsonObject[] blocks, string stopReason)
    {
        List<JsonObject> events = [new JsonObject { ["type"] = "message_start", ["message"] = Message(stopReason: null) }];
        for (int index = 0; index < blocks.Length; index++)
        {
            JsonObject block = blocks[index];
            if (string.Equals((string?)block["type"], "text", StringComparison.Ordinal))
            {
                events.Add(new JsonObject { ["type"] = "content_block_start", ["index"] = index, ["content_block"] = Text(string.Empty) });
                events.Add(new JsonObject { ["type"] = "content_block_delta", ["index"] = index, ["delta"] = new JsonObject { ["type"] = "text_delta", ["text"] = block["text"]!.DeepClone() } });
            }
            else
            {
                JsonObject start = new JsonObject { ["type"] = "tool_use", ["id"] = block["id"]!.DeepClone(), ["name"] = block["name"]!.DeepClone(), ["input"] = new JsonObject() };
                events.Add(new JsonObject { ["type"] = "content_block_start", ["index"] = index, ["content_block"] = start });
                events.Add(new JsonObject { ["type"] = "content_block_delta", ["index"] = index, ["delta"] = new JsonObject { ["type"] = "input_json_delta", ["partial_json"] = block["input"]!.ToJsonString() } });
            }

            events.Add(new JsonObject { ["type"] = "content_block_stop", ["index"] = index });
        }

        events.Add(new JsonObject { ["type"] = "message_delta", ["delta"] = new JsonObject { ["stop_reason"] = stopReason, ["stop_sequence"] = null }, ["usage"] = new JsonObject { ["output_tokens"] = 3 } });
        events.Add(new JsonObject { ["type"] = "message_stop" });
        return EventStream([.. events]);
    }

    // Server-sent events, each named by its type.
    private static string EventStream(JsonObject[] events)
    {
        StringBuilder stream = new StringBuilder();
        foreach (JsonObject item in events)
        {
            stream.Append("event: ").Append((string?)item["type"]).Append('\n').Append("data: ").Append(item.ToJsonString()).Append("\n\n");
        }

        return stream.ToString();
    }

    // The calls held on one phrase, released together.
    private sealed class Hold
    {
        private TaskCompletionSource _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private bool _forGood;

        public Channel<bool> Started { get; } = Channel.CreateUnbounded<bool>();

        public bool Holding => !Volatile.Read(ref _forGood);

        public Task Released => Volatile.Read(ref _released).Task;

        public void Release()
        {
            Interlocked.Exchange(ref _released, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
        }

        public void ReleaseForGood()
        {
            Volatile.Write(ref _forGood, true);
            Release();
        }
    }
}
