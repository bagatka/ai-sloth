using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: nooks nobody uses fall asleep and come back when used, with their files and an agent that
/// remembers. A busy nook stays awake beside an idle one that sleeps and wakes for its next message; a
/// nook wakes early on request; one asleep for long is evicted and comes back from its latest
/// checkpoint, through a ready copy when its setup left one; and one whose container is lost comes back
/// too; and one stays awake while someone watches its process. On the sleepy app, whose nooks sleep
/// after 8 idle seconds and are evicted after 20 asleep; the parts wait on those clocks, so they run at
/// the same time, each as someone else.
/// </summary>
public sealed class SleepJourney(SleepyControlPlane sleepy)
{
    // Losing a nook is noticed on the reconciler's next pass, then a new one starts.
    private static readonly TimeSpan Recovery = TimeSpan.FromMinutes(3);

    // Longer than the sleepy app's sleep period, 8 seconds, plus a pass of its sleep job, 10 seconds:
    // an unwatched nook would be asleep by then.
    private static readonly TimeSpan WatchedFor = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task Nooks_sleep_when_unused_and_come_back_with_their_files_and_an_agent_that_remembers()
    {
        ControlPlane app = await sleepy.StartedAsync();

        await Task.WhenAll(
            ABusyNookStaysAwakeWhileAnIdleOneSleepsAndWakesForItsNextMessageAsync(app),
            ANookWakesEarlyOnRequestAsync(app),
            ANookAsleepForLongIsEvictedAndComesBackFromItsLatestCheckpointAsync(app),
            AnEvictedNookComesBackThroughTheReadyCopyItsSlowSetupLeftAsync(app),
            ANookWhoseContainerIsLostComesBackFromItsCheckpointAsync(app),
            ANookStaysAwakeWhileSomeoneWatchesItsProcessAsync(app),
            AWorkspaceHasAtMostTwoNooksAwakeAsync(app));
    }

    private static async Task ABusyNookStaysAwakeWhileAnIdleOneSleepsAndWakesForItsNextMessageAsync(ControlPlane app)
    {
        using HttpClient alice = app.ClientFor("alice-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, alice);
        ChatSummary idle = await acme.StartChatAsync();
        ChatSummary busy = await acme.StartChatAsync();
        await using ChatWatch idleWatch = await ChatWatch.OpenAsync(alice, idle);
        await using ChatWatch busyWatch = await ChatWatch.OpenAsync(alice, busy);
        await acme.SendAsync(idle, "Please write hello.txt for me");
        await idleWatch.NextAsync("turn-ended");
        app.Model.ForgetHolds();
        await acme.SendAsync(busy, "wait for me");
        await app.Model.Holds.ReadAsync(TestContext.Current.CancellationToken);

        NookStatus asleep = await acme.StatusAsync(idle.NookId, status => status is NookStatus.Asleep, SleepyControlPlane.Sleep);
        NookSummary busyNook = await acme.NookAsync(busy.NookId);
        app.Model.Release();
        await busyWatch.NextAsync("turn-ended");
        await acme.SendAsync(idle, "What was my first message?");
        JsonElement restarted = await idleWatch.NextAsync("agent-restarted", SleepyControlPlane.Sleep);
        await idleWatch.NextAsync("turn-ended");
        int? kept = await acme.RunAsync(idle.NookId, "grep -q 'hi from the fake model' /work/hello.txt");

        Assert.Equal((NookStatus.Asleep, NookStatus.Ready), (asleep, busyNook.Status));
        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Contains(idleWatch.Seen, seen => string.Equals(seen.Type, "agent-update", StringComparison.Ordinal)
            && seen.Event.GetProperty("update").ToString().Contains("You first said: Please write hello.txt for me", StringComparison.Ordinal));
        Assert.Equal(0, kept);
    }

    private static async Task ANookWakesEarlyOnRequestAsync(ControlPlane app)
    {
        using HttpClient bob = app.ClientFor("bob-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, bob);
        ChatSummary chat = await acme.StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(bob, chat);
        await acme.SendAsync(chat, "say hello");
        await watch.NextAsync("turn-ended");
        NookStatus asleep = await acme.StatusAsync(chat.NookId, status => status is NookStatus.Asleep, SleepyControlPlane.Sleep);

        await Api.ExpectAsync(bob.SendPostAsync(Paths.Nook(chat.NookId) + "/wake", new { }), HttpStatusCode.NoContent);
        NookStatus awake = await acme.StatusAsync(chat.NookId, status => status is NookStatus.Ready, SleepyControlPlane.Sleep);

        Assert.Equal((NookStatus.Asleep, NookStatus.Ready), (asleep, awake));
    }

    private static async Task ANookAsleepForLongIsEvictedAndComesBackFromItsLatestCheckpointAsync(ControlPlane app)
    {
        using HttpClient carol = app.ClientFor("carol-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, carol);
        ChatSummary chat = await acme.StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(carol, chat);
        await acme.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("turn-ended");
        await acme.ChangeAsync(chat.NookId, "touch /tmp/outside-the-checkpoint");

        bool evicted = await app.SandboxGoneAsync(chat.NookId.Value, SleepyControlPlane.Eviction);
        await acme.SendAsync(chat, "What was my first message?");
        JsonElement restarted = await watch.NextAsync("agent-restarted", SleepyControlPlane.Sleep);
        await watch.NextAsync("turn-ended");
        int? restored = await acme.RunAsync(chat.NookId, "grep -q 'hi from the fake model' /work/hello.txt && test ! -e /tmp/outside-the-checkpoint");

        Assert.True(evicted);
        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Equal(0, restored);
    }

    private static async Task AnEvictedNookComesBackThroughTheReadyCopyItsSlowSetupLeftAsync(ControlPlane app)
    {
        using HttpClient dev = app.ClientFor("dev-" + Guid.CreateVersion7());
        SlowSetupRepository repository = await SlowSetupRepository.CreateAsync(app, dev);
        TestWorkspace acme = repository.Workspace;
        ChatSummary chat = await repository.StartChatAsync("docker");
        await using ChatWatch watch = await ChatWatch.OpenAsync(dev, chat);
        await acme.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("turn-ended", TimeSpan.FromMinutes(2));

        bool evicted = await app.SandboxGoneAsync(chat.NookId.Value, SleepyControlPlane.Eviction);
        await acme.SendAsync(chat, "What was my first message?");
        JsonElement started = await watch.NextAsync("setup-started", SleepyControlPlane.Sleep);
        JsonElement ended = await watch.NextAsync("setup-ended");
        await watch.NextAsync("turn-ended");
        int? kept = await acme.RunAsync(chat.NookId, "test -f /work/api/deps/installed && grep -q 'hi from the fake model' /work/hello.txt");

        Assert.True(evicted);
        Assert.True(started.GetProperty("fromReadyCopy").GetBoolean());
        Assert.True(TimeSpan.Parse(ended.GetProperty("took").GetString()!, CultureInfo.InvariantCulture) < TimeSpan.FromSeconds(SlowSetupRepository.SetupSeconds));
        Assert.Equal(0, kept);
    }

    private static async Task ANookStaysAwakeWhileSomeoneWatchesItsProcessAsync(ControlPlane app)
    {
        using HttpClient frank = app.ClientFor("frank-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, frank);
        NookSummary nook = await Api.ReadAsync<NookSummary>(frank.SendPostAsync(acme.Path + "/nooks", new { provider = "docker" }), HttpStatusCode.Created);
        ProcessSummary ticking = await NookProcesses.StartAsync(frank, nook.Id, "sh", "-c", "while true; do echo tick; sleep 1; done");

        bool outputKeptComing = await WatchAsync(frank, nook.Id, ticking.Id, WatchedFor);
        NookSummary watched = await acme.NookAsync(nook.Id);
        NookStatus afterwards = await acme.StatusAsync(nook.Id, status => status is NookStatus.Asleep, SleepyControlPlane.Sleep);

        Assert.True(outputKeptComing, "The process's output ended while it was watched.");
        Assert.Equal((NookStatus.Ready, NookStatus.Asleep), (watched.Status, afterwards));
    }

    // The sleepy app lets a workspace have two nooks awake. While both are at work, a third can't start;
    // once one of them isn't, a third starts, and that one falls asleep to make room.
    private static async Task AWorkspaceHasAtMostTwoNooksAwakeAsync(ControlPlane app)
    {
        using HttpClient grace = app.ClientFor("grace-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, grace);
        NookSummary first = await Api.ReadAsync<NookSummary>(grace.SendPostAsync(acme.Path + "/nooks", new { provider = "docker" }), HttpStatusCode.Created);
        NookSummary second = await Api.ReadAsync<NookSummary>(grace.SendPostAsync(acme.Path + "/nooks", new { provider = "docker" }), HttpStatusCode.Created);
        using CancellationTokenSource watchingFirst = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        using CancellationTokenSource watchingSecond = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<ProcessEvent> firstAtWork = await AtWorkAsync(grace, first.Id, watchingFirst.Token);
        await using IAsyncEnumerator<ProcessEvent> secondAtWork = await AtWorkAsync(grace, second.Id, watchingSecond.Token);

        Problem refused = await Api.ProblemAsync(grace.SendPostAsync(acme.Path + "/nooks", new { provider = "docker" }), HttpStatusCode.Conflict);
        await watchingFirst.CancelAsync();
        await firstAtWork.DisposeAsync();
        NookSummary third = await Api.EventuallyAsync(async () =>
        {
            using HttpResponseMessage response = await grace.SendPostAsync(acme.Path + "/nooks", new { provider = "docker" });
            if (response.StatusCode != HttpStatusCode.Created)
            {
                return null;
            }

            NookSummary created = await Api.ReadAsync<NookSummary>(response, HttpStatusCode.Created);
            return created;
        });
        NookStatus firstAfter = await acme.StatusAsync(first.Id, status => status is NookStatus.Asleep, SleepyControlPlane.Sleep);
        NookSummary secondAfter = await acme.NookAsync(second.Id);
        await watchingSecond.CancelAsync();
        await secondAtWork.DisposeAsync();

        Assert.Equal(NooksErrors.TooManyAwake.Code, refused.Code);
        Assert.NotEqual(first.Id, third.Id);
        Assert.Equal((NookStatus.Asleep, NookStatus.Ready), (firstAfter, secondAfter.Status));
    }

    // A person watching a process that keeps printing: the nook is at work while the watch is open.
    private static async Task<IAsyncEnumerator<ProcessEvent>> AtWorkAsync(HttpClient person, NookId nook, CancellationToken watching)
    {
        ProcessSummary ticking = await NookProcesses.StartAsync(person, nook, "sh", "-c", "while true; do echo tick; sleep 1; done");
        IAsyncEnumerator<ProcessEvent> output = NookProcesses.WatchAsync(person, nook, ticking.Id, fromOffset: 0, watching).GetAsyncEnumerator(watching);
        bool watched = await output.MoveNextAsync();
        Assert.True(watched, "The process's output never came.");
        return output;
    }

    // Reads the process's output for the time given, as a person watching it would; returns whether
    // output was still coming when they stopped.
    private static async Task<bool> WatchAsync(HttpClient person, NookId nook, ProcessId process, TimeSpan watchFor)
    {
        using CancellationTokenSource watching = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        await using IAsyncEnumerator<ProcessEvent> output = NookProcesses.WatchAsync(person, nook, process, fromOffset: 0, watching.Token).GetAsyncEnumerator(watching.Token);
        long started = TimeProvider.System.GetTimestamp();
        while (TimeProvider.System.GetElapsedTime(started) < watchFor)
        {
            bool more = await output.MoveNextAsync();
            if (!more || output.Current.Value is not ProcessOutput)
            {
                return false;
            }
        }

        await watching.CancelAsync();
        return true;
    }

    private static async Task ANookWhoseContainerIsLostComesBackFromItsCheckpointAsync(ControlPlane app)
    {
        using HttpClient erin = app.ClientFor("erin-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, erin);
        ChatSummary chat = await acme.StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(erin, chat);
        await acme.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("turn-ended");

        await app.LoseSandboxAsync(chat.NookId.Value);
        await acme.SendAsync(chat, "What was my first message?");
        JsonElement restarted = await watch.NextAsync("agent-restarted", Recovery);
        await watch.NextAsync("turn-ended");
        int? restored = await acme.RunAsync(chat.NookId, "grep -q 'hi from the fake model' /work/hello.txt");

        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Equal(0, restored);
    }
}
