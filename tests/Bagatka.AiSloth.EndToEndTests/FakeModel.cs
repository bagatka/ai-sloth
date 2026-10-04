using System;
using System.Collections.Concurrent;
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
/// A model provider at the HTTP boundary, speaking the Anthropic Messages API well enough for Claude
/// Code: the real agent runs in a real nook, only the model is fake. It answers by the last user
/// message's latest text: a tool result ends the turn, "write hello" asks to write <c>/work/hello.txt</c>, "wait"
/// holds the answer until <see cref="Release"/>, and anything else gets a short text.
/// </summary>
internal sealed class FakeModel : IAsyncDisposable
{
    public const string ApiKey = "e2e-model-key";

    private readonly WebApplication _app;
    private readonly Channel<bool> _holds = Channel.CreateUnbounded<bool>();
    private TaskCompletionSource _released = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    private FakeModel(WebApplication app)
    {
        _app = app;
    }

    /// <summary>Where the model gateway forwards to, such as <c>http://127.0.0.1:41235</c>.</summary>
    public Uri Url { get; private set; } = new Uri("http://localhost");

    /// <summary>One item for every call that started holding, so a test knows the agent's turn is running.</summary>
    public ChannelReader<bool> Holds => _holds.Reader;

    /// <summary>Forgets holds earlier tests left, so the next one read is the caller's own.</summary>
    public void ForgetHolds()
    {
        while (_holds.Reader.TryRead(out _))
        {
        }
    }

    /// <summary>The credentials each message call carried, as the gateway forwarded them.</summary>
    public ConcurrentQueue<(string? ApiKey, string? Authorization)> Credentials { get; } = new ConcurrentQueue<(string? ApiKey, string? Authorization)>();

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
        await app.StartAsync();
        IServerAddressesFeature addresses = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("Kestrel reported no addresses.");
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
        JsonNode request = await JsonNode.ParseAsync(context.Request.Body, cancellationToken: context.RequestAborted)
            ?? throw new InvalidOperationException("The call had no body.");
        JsonArray messages = request["messages"]!.AsArray();
        JsonNode? lastUser = messages.LastOrDefault(message => string.Equals((string?)message!["role"], "user", StringComparison.Ordinal));
        bool tools = request["tools"] is JsonArray offered && offered.Any(tool => string.Equals((string?)tool!["name"], "Write", StringComparison.Ordinal));

        JsonObject[] blocks;
        string stopReason;
        if (Blocks(lastUser).Any(block => string.Equals((string?)block["type"], "tool_result", StringComparison.Ordinal)))
        {
            (blocks, stopReason) = ([Text("Done.")], "end_turn");
        }
        else if (tools && Says(lastUser, "write hello"))
        {
            JsonObject write = new JsonObject { ["type"] = "tool_use", ["id"] = "toolu_e2e", ["name"] = "Write", ["input"] = new JsonObject { ["file_path"] = "/work/hello.txt", ["content"] = "hi from the fake model\n" } };
            (blocks, stopReason) = ([write], "tool_use");
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

    private static JsonObject Text(string text)
    {
        return new JsonObject { ["type"] = "text", ["text"] = text };
    }

    private static IEnumerable<JsonObject> Blocks(JsonNode? message)
    {
        return message?["content"] is JsonArray content ? content.OfType<JsonObject>() : [];
    }

    // Only the latest text counts: after a stop, Claude Code sends the stopped prompt and the next one
    // as one user message.
    private static bool Says(JsonNode? message, string words)
    {
        string? text = message?["content"] is JsonValue plain
            ? plain.GetValue<string>()
            : Blocks(message).LastOrDefault(block => block["text"] is not null)?["text"]?.GetValue<string>();
        return text?.Contains(words, StringComparison.OrdinalIgnoreCase) == true;
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
        StringBuilder stream = new StringBuilder();
        foreach (JsonObject item in events)
        {
            stream.Append("event: ").Append((string?)item["type"]).Append('\n').Append("data: ").Append(item.ToJsonString()).Append("\n\n");
        }

        return stream.ToString();
    }
}
