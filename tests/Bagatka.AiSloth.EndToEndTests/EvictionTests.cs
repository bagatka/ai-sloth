using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A nook asleep for long is evicted, and comes back from its latest checkpoint at its next use.
/// </summary>
public sealed class EvictionTests(SleepyControlPlane sleepy)
{
    [Fact]
    public async Task A_nook_asleep_for_long_is_evicted_and_comes_back_from_its_latest_checkpoint()
    {
        ControlPlane app = await sleepy.StartedAsync();
        using TestChats chats = new TestChats(app, "docker");
        ChatSummary chat = await chats.StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(chats.Person, chat);
        await chats.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("checkpoint-saved");
        await NookProcesses.ExitCodeAsync(chats.Person, chat.NookId, "touch", "/tmp/outside-the-checkpoint");

        NookStatus evicted = await chats.StatusAsync(chat, status => status is NookStatus.Evicted, SleepyControlPlane.Eviction);
        await chats.SendAsync(chat, "What was my first message?");
        JsonElement restarted = await watch.NextAsync("agent-restarted", SleepyControlPlane.Sleep);
        await watch.NextAsync("turn-ended");
        int? restored = await NookProcesses.ExitCodeAsync(chats.Person, chat.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");
        int? fresh = await NookProcesses.ExitCodeAsync(chats.Person, chat.NookId, "test", "!", "-e", "/tmp/outside-the-checkpoint");

        Assert.Equal(NookStatus.Evicted, evicted);
        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Equal(0, restored);
        Assert.Equal(0, fresh);
    }
}
