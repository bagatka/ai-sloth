using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A nook whose agent works never falls asleep under it, while an idle one beside it does.
/// </summary>
public sealed class KeepAwakeTests(SleepyControlPlane sleepy)
{
    [Fact]
    public async Task A_nook_whose_agent_works_stays_awake_while_an_idle_one_falls_asleep()
    {
        ControlPlane app = await sleepy.StartedAsync();
        using TestChats chats = new TestChats(app, "docker");
        ChatSummary idle = await chats.StartChatAsync();
        ChatSummary busy = await chats.StartChatAsync();
        await using ChatWatch idleWatch = await ChatWatch.OpenAsync(chats.Person, idle);
        await using ChatWatch busyWatch = await ChatWatch.OpenAsync(chats.Person, busy);
        await chats.SendAsync(idle, "say hello");
        await idleWatch.NextAsync("checkpoint-saved");
        await chats.SendAsync(busy, "wait for me");
        await chats.Model.Holds.ReadAsync(TestContext.Current.CancellationToken);

        await chats.StatusAsync(idle, status => status is NookStatus.Stopped, SleepyControlPlane.Sleep);
        NookSummary busyNook = await chats.NookAsync(busy);
        chats.Model.Release();
        await busyWatch.NextAsync("turn-ended");

        Assert.Equal(NookStatus.Running, busyNook.Status);
    }
}
