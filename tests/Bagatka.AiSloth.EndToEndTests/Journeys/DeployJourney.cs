using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: a deploy replaces the control plane in the middle of a chat's turn, and the chat goes on as
/// if nothing happened. The running WebApi is told to stop while the agent's model call is still
/// streaming through it; it hands its work over and lets the call finish, the agent keeps running in
/// its nook, the chat's watch resumes on the new WebApi, the turn ends as it would have, and the next
/// one works. The new WebApi also deletes a sandbox that no nook records, as a reset database leaves
/// behind. On an app of its own, so no other journey runs into the swap.
/// </summary>
public sealed class DeployJourney(DeployedControlPlane deployed)
{
    // The new WebApi starts, its daemons dial in, and it resumes the turn: well within this.
    private static readonly TimeSpan Swap = TimeSpan.FromMinutes(2);

    [Fact]
    public async Task A_chat_goes_on_as_if_nothing_happened_while_a_deploy_replaces_the_control_plane()
    {
        ControlPlane app = await deployed.StartedAsync();
        using HttpClient alice = app.ClientFor("alice-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, alice);
        ChatSummary chat = await acme.StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(alice, chat);
        await acme.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("turn-ended");
        app.Model.ForgetHolds();
        await acme.SendAsync(chat, "wait for me");
        await app.Model.Holds.ReadAsync(TestContext.Current.CancellationToken);
        Guid orphan = await app.LeaveOrphanSandboxAsync();

        await app.ReplaceWebApiAsync(whileStopping: app.Model.ReleaseForGood);
        JsonElement ended = await watch.NextAsync("turn-ended", Swap);
        await acme.SendAsync(chat, "say hello");
        JsonElement next = await watch.NextAsync("turn-ended");
        bool orphanGone = await app.SandboxGoneAsync(orphan, Swap);

        Assert.Equal("end_turn", ended.GetProperty("stopReason").GetString());
        Assert.Equal(0, app.Model.Cut);
        Assert.True(watch.Resumed > 0, "The chat's watch never moved to the new WebApi.");
        Assert.DoesNotContain(watch.Seen, seen => string.Equals(seen.Type, "agent-restarted", StringComparison.Ordinal));
        Assert.Equal("end_turn", next.GetProperty("stopReason").GetString());
        Assert.True(orphanGone, "The new WebApi left a sandbox no nook records.");
    }
}
