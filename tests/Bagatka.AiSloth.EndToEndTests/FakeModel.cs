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

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A model provider at the HTTP boundary: the real agent runs in a real nook, only the model is fake.
/// It speaks the Anthropic Messages API well enough for Claude Code, answering by the last user
/// message's latest text: a tool result last ends the turn, "write hello" asks to write <c>/work/hello.txt</c>, a
/// request to prepare a chat writes a <c>.agents/setup</c> that needs what was installed by hand
/// (<c>/opt/by-hand</c>), and a failed test of it fixes it to install that, "wait"
/// holds the answer until <see cref="Release"/>, "first message" answers with the conversation's first
/// message, and anything else gets a short text. It speaks
/// OpenAI's Responses API well enough for Codex and pi, answering every call with a short text.
/// </summary>
internal sealed class FakeModel : IAsyncDisposable
{
    /// <summary>The key tests give workspaces' Anthropic accounts; the gateway forwards it.</summary>
    public const string ApiKey = "e2e-model-key";

    private readonly WebApplication _app;
    private readonly Channel<bool> _holds = Channel.CreateUnbounded<bool>();
    private TaskCompletionSource _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private FakeModel(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Its Anthropic API, as an account names it: the root, such as <c>http://127.0.0.1:41235/</c>.</summary>
    public Uri Url { get; private set; } = new Uri("http://localhost");

    /// <summary>Its OpenAI API, as an account names it: <c>…/v1</c>.</summary>
    public Uri OpenAIUrl => new Uri(Url, "v1");

    /// <summary>One item for every call that started holding, so a test knows the agent's turn is running.</summary>
    public ChannelReader<bool> Holds => _holds.Reader;

    /// <summary>Forgets holds earlier tests left, so the next one read is the caller's own.</summary>
    public void ForgetHolds()
    {
        while (_holds.Reader.TryRead(out _))
        {
        }
    }

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
        await app.StartAsync();
        IServerAddressesFeature? addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>();
        if (addresses is null)
        {
            throw new InvalidOperationException("Kestrel reported no addresses.");
        }

        model.Url = new Uri(addresses.Addresses.Single());
        return model;
    }

    /// <summary>Answers the calls held so far; later ones hold until the next release.</summary>
    public void Release()
    {
        Interlocked.Exchange(ref _released, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)).TrySetResult();
    }

    public async ValueTask DisposeAsync()
    {
        Release();
        await _app.DisposeAsync();
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
        else if (Says(lastUser, "wait"))
        {
            Task released = Volatile.Read(ref _released).Task;
            _holds.Writer.TryWrite(true);
            await released.WaitAsync(context.RequestAborted);
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

    private static JsonObject Text(string text)
    {
        return new JsonObject { ["type"] = "text", ["text"] = text };
    }

    // What the agent runs when asked to fix its setup after a failed test, or to prepare the project:
    // each once in a conversation. Any text of the message counts, because Claude Code sends a
    // later turn's prompt with the earlier turn's tool result and adds reminders.
    private static string? SetupCommand(JsonArray messages, JsonNode? message)
    {
        string text = message?["content"] is JsonValue plain
            ? plain.GetValue<string>()
            : string.Join("\n", Blocks(message).Select(block => block["text"]?.GetValue<string>()));
        (string Asked, string Command)[] steps =
        [
            ("in a fresh nook, with only this chat's files, and it failed", "printf '#!/bin/sh\\nmkdir -p /opt && touch /opt/by-hand\\n' > /work/.agents/setup"),
            ("Write a setup for the code in your working directory", "mkdir -p /work/.agents && printf '#!/bin/sh\\ntest -f /opt/by-hand\\n' > /work/.agents/setup && chmod +x /work/.agents/setup"),
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
}
