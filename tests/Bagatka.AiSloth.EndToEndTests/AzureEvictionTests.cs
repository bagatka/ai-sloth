using System.Text.Json;
using System.Threading.Tasks;
using Bagatka.AiSloth.Chats.Contracts;
using Bagatka.AiSloth.Nooks.Contracts;
using Xunit;

namespace Bagatka.AiSloth.EndToEndTests;

/// <summary>
/// A nook on Azure asleep for long is evicted, and comes back from its latest checkpoint, when asked
/// for (<see cref="AzureControlPlane"/>).
/// </summary>
public sealed class AzureEvictionTests(AzureControlPlane azure)
{
    [Fact]
    public async Task A_nook_on_Azure_asleep_for_long_is_evicted_and_comes_back_from_its_latest_checkpoint()
    {
        ControlPlane? app = await azure.StartedAsync();
        Assert.SkipWhen(app is null, "Set BAGATKA_AZURE_SANDBOXES_GROUP and BAGATKA_NGROK_ENV_FILE to run nooks on Azure.");
        using TestChats chats = new TestChats(app, "azure");
        ChatSummary chat = await chats.StartChatAsync();
        await using ChatWatch watch = await ChatWatch.OpenAsync(chats.Person, chat);
        await chats.SendAsync(chat, "Please write hello.txt for me");
        await watch.NextAsync("checkpoint-saved", AzureControlPlane.FirstTurn);
        await NookProcesses.ExitCodeAsync(chats.Person, chat.NookId, "touch", "/tmp/outside-the-checkpoint");

        NookStatus evicted = await chats.StatusAsync(chat, status => status is NookStatus.Evicted, AzureControlPlane.Eviction);
        await chats.SendAsync(chat, "What was my first message?");
        JsonElement restarted = await watch.NextAsync("agent-restarted", AzureControlPlane.FirstTurn);
        await watch.NextAsync("turn-ended");
        int? restored = await NookProcesses.ExitCodeAsync(chats.Person, chat.NookId, "grep", "-q", "hi from the fake model", "/work/hello.txt");
        int? fresh = await NookProcesses.ExitCodeAsync(chats.Person, chat.NookId, "test", "!", "-e", "/tmp/outside-the-checkpoint");

        Assert.Equal(NookStatus.Evicted, evicted);
        Assert.True(restarted.GetProperty("remembers").GetBoolean());
        Assert.Equal(0, restored);
        Assert.Equal(0, fresh);
    }
}
