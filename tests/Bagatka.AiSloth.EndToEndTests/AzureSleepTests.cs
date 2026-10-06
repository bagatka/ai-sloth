using System;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A chat on Azure, when asked for (<see cref="AzureControlPlane"/>): the agent works and Docker runs
/// in the nook, which sleeps with its memory and wakes with its agent still running.
/// </summary>
public sealed class AzureSleepTests(AzureControlPlane azure)
{
    [Fact]
    public async Task A_nook_on_Azure_runs_its_agent_and_Docker_and_sleeps_and_wakes_with_its_memory()
    {
        ControlPlane? app = await azure.StartedAsync();
        Assert.SkipWhen(app is null, "Set BAGATKA_AZURE_SANDBOXES_GROUP and BAGATKA_NGROK_ENV_FILE to run nooks on Azure.");
        using TestChats chats = new TestChats(app, "azure");
        ChatSummary chat = await chats.StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(chats.Person, chat);
        await chats.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("checkpoint-saved", AzureControlPlane.FirstTurn);
        int? docker = await NookProcesses.ExitCodeAsync(chats.Person, chat.NookId, "docker", "run", "--rm", "busybox", "true");

        NookStatus asleep = await chats.StatusAsync(chat, status => status is NookStatus.Paused, AzureControlPlane.Sleep);
        await chats.SendAsync(chat, "What was my first message?");
        await watch.NextAsync("turn-ended", AzureControlPlane.Sleep);

        Assert.Equal(0, docker);
        Assert.Equal(NookStatus.Paused, asleep);
        Assert.DoesNotContain(watch.Seen, seen => string.Equals(seen.Type, "agent-restarted", StringComparison.Ordinal));
        Assert.Contains(watch.Seen, seen => string.Equals(seen.Type, "agent-update", StringComparison.Ordinal)
            && seen.Event.GetProperty("update").ToString().Contains("You first said: Please write hello.txt for me", StringComparison.Ordinal));
    }
}
