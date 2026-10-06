using System;
using System.Globalization;
using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// An evicted nook whose slow setup left a ready copy comes back through it, with its checkpoint's
/// files and the setup's work kept.
/// </summary>
public sealed class ReadyCopyEvictionTests(SleepyControlPlane sleepy)
{
    [Fact]
    public async Task An_evicted_nook_comes_back_through_the_ready_copy_its_slow_setup_left()
    {
        ControlPlane app = await sleepy.StartedAsync();
        using TestChats chats = new TestChats(app, "docker");
        SlowSetupRepository repository = await SlowSetupRepository.CreateAsync(app, chats.Person);
        ChatSummary chat = await repository.StartChatAsync("docker");
        await using ChatWatch watch = await ChatWatch.OpenAsync(chats.Person, chat);
        await chats.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("checkpoint-saved", TimeSpan.FromMinutes(2));

        NookStatus evicted = await chats.StatusAsync(chat, status => status is NookStatus.Evicted, SleepyControlPlane.Eviction);
        await chats.SendAsync(chat, "What was my first message?");
        JsonElement started = await watch.NextAsync("setup-started", SleepyControlPlane.Sleep);
        JsonElement ended = await watch.NextAsync("setup-ended");
        await watch.NextAsync("turn-ended");
        int? installed = await NookProcesses.ExitCodeAsync(chats.Person, chat.NookId, "test", "-f", "/work/api/deps/installed");
        int? restored = await NookProcesses.ExitCodeAsync(chats.Person, chat.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");

        Assert.Equal(NookStatus.Evicted, evicted);
        Assert.True(started.GetProperty("fromReadyCopy").GetBoolean());
        Assert.True(TimeSpan.Parse(ended.GetProperty("took").GetString()!, CultureInfo.InvariantCulture) < TimeSpan.FromSeconds(SlowSetupRepository.SetupSeconds));
        Assert.Equal(0, installed);
        Assert.Equal(0, restored);
    }
}
