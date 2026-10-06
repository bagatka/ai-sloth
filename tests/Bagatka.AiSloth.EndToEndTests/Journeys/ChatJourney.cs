using System;
using System.IO;
using System.IO.Compression;
using System.Formats.Tar;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Bagatka.Foundation;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: a chat with the real agent, Claude Code, in a real nook. <c>sloth</c> shows its work until
/// its turn ends; everyone sees each step; every turn ends with a checkpoint of the files; a message
/// sent while the agent works joins its turn, and stopping ends the turn. Only the model is fake.
/// </summary>
public sealed class ChatJourney(ControlPlane app) : IDisposable
{
    private readonly HttpClient _alice = app.ClientFor("alice-" + Guid.CreateVersion7());

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_chat_shows_the_agents_work_saves_its_files_and_takes_messages_any_time()
    {
        await SlothShowsTheAgentsWorkUntilItsTurnEndsAsync();
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, _alice);
        ChatSummary chat = await acme.StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(_alice, chat);
        await EveryoneSeesEachStepAndEachTurnSavesTheFilesAsync(acme, chat, watch);
        await AMessageSentWhileTheAgentWorksJoinsItsTurnAsync(acme, chat, watch);
        await StoppingEndsTheTurnAndTheNextMessageWorksAsync(acme, chat, watch);
        await EveryChatGetsANookOfItsOwnAsync(acme, chat);
    }

    public void Dispose()
    {
        _alice.Dispose();
    }

    private async Task SlothShowsTheAgentsWorkUntilItsTurnEndsAsync()
    {
        await using SlothCli sloth = new SlothCli("erin-" + Guid.CreateVersion7());
        await sloth.RunAsync("host", "add", app.WebApiUrl.AbsoluteUri);
        await sloth.RunWithInputAsync(FakeModel.ApiKey + "\n", "account", "add", "anthropic-api-key", "--name", "Fake", "--endpoint", app.Model.Url.AbsoluteUri);

        int chatted = await sloth.RunAsync("chat", "Please write hello.txt for me", "--harness", "claude-code");
        string chat = sloth.Output;
        int listed = await sloth.RunAsync("chat", "list");

        Assert.Equal(0, chatted);
        Assert.Matches("^Chat [0-9a-f]{6} · Claude Code · Fake · docker", chat);
        Assert.Contains("› You: Please write hello.txt for me\n  ▸ Write hello.txt\nDone.\n", chat, StringComparison.Ordinal);
        Assert.Contains("(files saved as checkpoint 1)", chat, StringComparison.Ordinal);
        Assert.Equal(0, listed);
        Assert.StartsWith(chat[5..11] + "  claude-code", sloth.Output, StringComparison.Ordinal);
    }

    private async Task EveryoneSeesEachStepAndEachTurnSavesTheFilesAsync(TestWorkspace acme, ChatSummary chat, ChatWatch watch)
    {
        ChatMessage sent = await acme.SendAsync(chat, "Please write hello.txt for me");
        JsonElement first = await watch.NextAsync("checkpoint-saved");
        await acme.ChangeAsync(chat.NookId, "echo changed > /work/hello.txt");
        await acme.SendAsync(chat, "say hello");
        JsonElement second = await watch.NextAsync("checkpoint-saved");
        Page<CheckpointSummary> listed = await Api.ReadAsync<Page<CheckpointSummary>>(_alice.SendGetAsync(Paths.Nook(chat.NookId) + "/checkpoints"), HttpStatusCode.OK);
        string? then = await HelloAsync(Paths.Nook(chat.NookId) + "/download?checkpoint=1");
        string? now = await HelloAsync(Paths.Nook(chat.NookId) + "/download?checkpoint=2");

        Assert.Equal(["message-sent", "turn-started"], watch.SeenOfChat.Take(2).Select(seen => seen.Type), StringComparer.Ordinal);
        Assert.Equal(sent.Id.Value, watch.SeenOfChat[0].Event.GetProperty("messageId").GetGuid());
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "agent-update", StringComparison.Ordinal)
            && string.Equals(seen.Event.GetProperty("update").GetProperty("sessionUpdate").GetString(), "tool_call", StringComparison.Ordinal));
        Assert.Equal((1, 2), (first.GetProperty("number").GetInt32(), second.GetProperty("number").GetInt32()));
        Assert.Equal([2, 1], listed.Items.Select(checkpoint => checkpoint.Number));
        Assert.Equal("Please write hello.txt for me", listed.Items[1].Note);
        Assert.Equal(("hi from the fake model\n", "changed\n"), (then, now));
        Assert.All(app.Model.Credentials, credentials => Assert.Equal((FakeModel.ApiKey, (string?)null), (credentials.ApiKey, credentials.Authorization)));
    }

    private async Task AMessageSentWhileTheAgentWorksJoinsItsTurnAsync(TestWorkspace acme, ChatSummary chat, ChatWatch watch)
    {
        app.Model.ForgetHolds();
        await acme.SendAsync(chat, "wait for me");
        await app.Model.Holds.ReadAsync(Ct);

        ChatMessage second = await acme.SendAsync(chat, "and then say hello");
        JsonElement steered = await watch.NextAsync("message-steered");
        app.Model.Release();
        JsonElement ended = await watch.NextAsync("turn-ended");

        Assert.Equal(second.Id.Value, steered.GetProperty("messageId").GetGuid());
        Assert.Equal("end_turn", ended.GetProperty("stopReason").GetString());
    }

    private async Task StoppingEndsTheTurnAndTheNextMessageWorksAsync(TestWorkspace acme, ChatSummary chat, ChatWatch watch)
    {
        app.Model.ForgetHolds();
        await acme.SendAsync(chat, "wait for me");
        await app.Model.Holds.ReadAsync(Ct);

        await Api.ExpectAsync(_alice.SendPostAsync(Paths.Chat(chat) + "/stop", new { }), HttpStatusCode.NoContent);
        JsonElement stopped = await watch.NextAsync("turn-ended");
        await acme.SendAsync(chat, "say hello");
        JsonElement next = await watch.NextAsync("turn-ended");
        ChatSummary after = await Api.ReadAsync<ChatSummary>(_alice.SendGetAsync(Paths.Chat(chat)), HttpStatusCode.OK);

        Assert.Equal("cancelled", stopped.GetProperty("stopReason").GetString());
        Assert.Equal("end_turn", next.GetProperty("stopReason").GetString());
        Assert.False(after.Working);
    }

    // One chat, one nook, one agent: agents never work on each other's files.
    private async Task EveryChatGetsANookOfItsOwnAsync(TestWorkspace acme, ChatSummary first)
    {
        ChatSummary second = await acme.StartChatAsync();
        await acme.SendAsync(second, "say hello");

        Page<ChatSummary> chats = await Api.ReadAsync<Page<ChatSummary>>(_alice.SendGetAsync(acme.Path + "/chats"), HttpStatusCode.OK);

        Assert.NotEqual(first.NookId, second.NookId);
        Assert.Equal([second.Id, first.Id], chats.Items.Select(chat => chat.Id));
    }

    // hello.txt in a downloaded archive of /work, or null when it has none.
    private async Task<string?> HelloAsync(string download)
    {
        using HttpResponseMessage response = await _alice.GetAsync(new Uri(download, UriKind.Relative), Ct);
        await Api.ExpectAsync(response, HttpStatusCode.OK);
        await using Stream body = await response.Content.ReadAsStreamAsync(Ct);
        await using GZipStream gzip = new GZipStream(body, CompressionMode.Decompress);
        await using TarReader tar = new TarReader(gzip);
        TarEntry? entry = await tar.GetNextEntryAsync(copyData: false, Ct);
        while (entry is not null)
        {
            if (entry.Name is "./hello.txt" && entry.DataStream is not null)
            {
                using StreamReader reader = new StreamReader(entry.DataStream);
                return await reader.ReadToEndAsync(Ct);
            }

            entry = await tar.GetNextEntryAsync(copyData: false, Ct);
        }

        return null;
    }
}
