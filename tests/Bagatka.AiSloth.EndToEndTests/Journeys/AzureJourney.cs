using System;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// Journey: nooks on Azure, when asked for (<see cref="AzureControlPlane"/>). A nook runs its agent and
/// Docker, gets no Azure token from its sandbox, sleeps with its memory, and wakes with its agent still
/// running; one asleep for long is
/// evicted and comes back from its latest checkpoint; and the next chat with a repository sets up from
/// the ready copy its slow setup left. The parts wait on Azure, so they run at the same time.
/// </summary>
public sealed class AzureJourney(AzureControlPlane azure)
{
    [Fact]
    public async Task Nooks_on_Azure_work_sleep_and_come_back()
    {
        ControlPlane? app = await azure.StartedAsync();
        Assert.SkipWhen(app is null, "Set BAGATKA_AZURE_SANDBOXES_GROUP and BAGATKA_NGROK_ENV_FILE to run nooks on Azure.");

        await Task.WhenAll(
            ANookRunsItsAgentAndDockerAndSleepsAndWakesWithItsMemoryAsync(app),
            ANookAsleepForLongIsEvictedAndComesBackFromItsLatestCheckpointAsync(app),
            TheNextChatWithARepositorySetsUpFromTheReadyCopyItsSlowSetupLeftAsync(app));
    }

    private static async Task ANookRunsItsAgentAndDockerAndSleepsAndWakesWithItsMemoryAsync(ControlPlane app)
    {
        using HttpClient alice = app.ClientFor("alice-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, alice);
        ChatSummary chat = await acme.StartChatAsync(provider: "azure");
        await using ChatWatch watch = await ChatWatch.OpenAsync(alice, chat);
        await acme.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("turn-ended", AzureControlPlane.FirstTurn);
        int? docker = await acme.RunAsync(chat.NookId, "docker run --rm busybox true");

        // Azure offers every sandbox an identity endpoint; the group has no identity, so it gives nothing.
        int? tokenRefused = await acme.RunAsync(chat.NookId, "test \"$(curl -s -m 10 -o /dev/null -w '%{http_code}' \"$IDENTITY_ENDPOINT?api-version=2019-08-01&resource=https://management.azure.com/\" -H \"X-IDENTITY-HEADER: $IDENTITY_HEADER\")\" != 200");

        NookStatus asleep = await acme.StatusAsync(chat.NookId, status => status is NookStatus.Asleep, AzureControlPlane.Sleep);
        await acme.SendAsync(chat, "What was my first message?");
        await watch.NextAsync("turn-ended", AzureControlPlane.Sleep);

        Assert.Equal(0, docker);
        Assert.Equal(0, tokenRefused);
        Assert.Equal(NookStatus.Asleep, asleep);
        Assert.DoesNotContain(watch.Seen, seen => string.Equals(seen.Type, "agent-restarted", StringComparison.Ordinal));
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "agent-update", StringComparison.Ordinal)
            && seen.Event.GetProperty("update").ToString().Contains("You first said: Please write hello.txt for me", StringComparison.Ordinal));
    }

    private static async Task ANookAsleepForLongIsEvictedAndComesBackFromItsLatestCheckpointAsync(ControlPlane app)
    {
        using HttpClient bob = app.ClientFor("bob-" + Guid.CreateVersion7());
        TestWorkspace acme = await TestWorkspace.CreateAsync(app, bob);
        ChatSummary chat = await acme.StartChatAsync(provider: "azure");
        await using ChatWatch watch = await ChatWatch.OpenAsync(bob, chat);
        await acme.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("turn-ended", AzureControlPlane.FirstTurn);
        await acme.ChangeAsync(chat.NookId, "touch /tmp/outside-the-checkpoint");

        bool evicted = await AzureControlPlane.SandboxGoneAsync(app, chat.NookId.Value, AzureControlPlane.Eviction);
        await acme.SendAsync(chat, "What was my first message?");
        JsonElement restarted = await watch.NextAsync("agent-restarted", AzureControlPlane.FirstTurn);
        await watch.NextAsync("turn-ended");
        int? restored = await acme.RunAsync(chat.NookId, "grep -q 'hi from the fake model' /work/hello.txt && test ! -e /tmp/outside-the-checkpoint");

        Assert.True(evicted);
        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Equal(0, restored);
    }

    private static async Task TheNextChatWithARepositorySetsUpFromTheReadyCopyItsSlowSetupLeftAsync(ControlPlane app)
    {
        using HttpClient dev = app.ClientFor("dev-" + Guid.CreateVersion7());
        SlowSetupRepository repository = await SlowSetupRepository.CreateAsync(app, dev);
        ChatSummary first = await repository.StartChatAsync("azure");
        await using ChatWatch firstWatch = await ChatWatch.OpenAsync(dev, first);
        await repository.Workspace.SendAsync(first, "Please write hello.txt for me");
        await firstWatch.NextAsync("turn-ended", AzureControlPlane.FirstTurn);

        ChatSummary second = await repository.StartChatAsync("azure");
        await using ChatWatch watch = await ChatWatch.OpenAsync(dev, second);
        JsonElement started = await watch.NextAsync("setup-started", AzureControlPlane.FirstTurn);
        JsonElement ended = await watch.NextAsync("setup-ended");
        int? installed = await repository.Workspace.RunAsync(second.NookId, "test -f /work/api/deps/installed");

        Assert.True(started.GetProperty("fromReadyCopy").GetBoolean());
        Assert.True(TimeSpan.Parse(ended.GetProperty("took").GetString()!, CultureInfo.InvariantCulture) < TimeSpan.FromSeconds(SlowSetupRepository.SetupSeconds));
        Assert.Equal(0, installed);
    }
}
