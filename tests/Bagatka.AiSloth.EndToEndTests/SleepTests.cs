using System;
using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Nooks nobody uses fall asleep, and wake for their next use, or early, with an agent that
/// remembers. On Docker a sleeping nook keeps its files, not its memory.
/// </summary>
public sealed class SleepTests(SleepyControlPlane sleepy)
{
    [Fact]
    public async Task A_nook_nobody_uses_falls_asleep_and_wakes_for_the_next_message_with_its_files_and_an_agent_that_remembers()
    {
        ControlPlane app = await sleepy.StartedAsync();
        using TestChats chats = new TestChats(app, "docker");
        ChatSummary chat = await chats.StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(chats.Person, chat);
        await chats.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("checkpoint-saved");

        NookStatus asleep = await chats.StatusAsync(chat, status => status is NookStatus.Stopped, SleepyControlPlane.Sleep);
        await chats.SendAsync(chat, "What was my first message?");
        JsonElement restarted = await watch.NextAsync("agent-restarted", SleepyControlPlane.Sleep);
        await watch.NextAsync("turn-ended");
        int? kept = await NookProcesses.ExitCodeAsync(chats.Person, chat.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");

        Assert.Equal(NookStatus.Stopped, asleep);
        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "agent-update", StringComparison.Ordinal)
            && seen.Event.GetProperty("update").ToString().Contains("You first said: Please write hello.txt for me", StringComparison.Ordinal));
        Assert.Equal(0, kept);
    }

    [Fact]
    public async Task Waking_a_nook_early_gives_it_compute_before_any_message()
    {
        ControlPlane app = await sleepy.StartedAsync();
        using TestChats chats = new TestChats(app, "docker");
        ChatSummary chat = await chats.StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(chats.Person, chat);
        await chats.SendAsync(chat, "say hello");
        await watch.NextAsync("checkpoint-saved");
        await chats.StatusAsync(chat, status => status is NookStatus.Stopped, SleepyControlPlane.Sleep);

        await Api.ExpectAsync(chats.Person.SendPostAsync(string.Create(CultureInfo.InvariantCulture, $"/nooks/{chat.NookId.Value}/wake"), new { }), HttpStatusCode.NoContent);
        NookStatus awake = await chats.StatusAsync(chat, status => status is NookStatus.Running, SleepyControlPlane.Sleep);

        Assert.Equal(NookStatus.Running, awake);
    }
}
